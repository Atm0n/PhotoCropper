using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Export;
using PhotoCropper.Core.IO;
using PhotoCropper.Core.Models;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Models;
using PhotoCropper.Gui.Services;

namespace PhotoCropper.Gui;

internal sealed partial class MainWindow
{
    private void Window_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void Window_Drop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer == null) return;

        var asyncTransfer = e.DataTransfer as IAsyncDataTransfer;
        var files = asyncTransfer != null ? await asyncTransfer.TryGetFilesAsync() : null;
        if (files == null) return;

        var rawPaths = files.Select(f => f.Path.LocalPath);
        var collected = ImageFileCollector.CollectFiles(rawPaths, recursive: true);
        if (collected.Count > 0)
        {
            await LoadScansFromPathsAsync(collected);
        }
    }

    private async Task LoadScansFromPathsAsync(IEnumerable<string> paths)
    {
        undoHistory.Clear();
        var options = GetDetectionOptionsFromUi();
        var settings = SettingsManager.Instance.Settings;
        var newSessions = new List<ScanSessionItem>();

        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            bool isAlreadyExported = ExportPathResolver.IsScanExported(
                path,
                settings.CustomOutputDirectory,
                settings.WorkDirectory);

            newSessions.Add(new ScanSessionItem(path, options, isSaved: isAlreadyExported, isModified: !isAlreadyExported));
        }

        _sessionManager.ReplaceAll(newSessions);
        UpdateEmptyStateVisibility();
        await LoadPhotosToGuiAsync();
    }

    private async void BtnOpenFiles_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;

        var fileResult = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.GetString(ResourceKeys.BtnOpenScans, "Select Files"),
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
            AllowMultiple = true
        });

        if (fileResult.Count > 0)
        {
            await LoadScansFromPathsAsync(fileResult.Select(f => f.Path.LocalPath));
        }
    }

    private async Task LoadPhotosToGuiAsync()
    {
        UpdateEmptyStateVisibility();
        if (ScanSessions.Count == 0 || isLoading) return;

        var session = ScanSessions[CurrentIndex];
        string fileName = Path.GetFileName(session.FilePath);
        string processingMsg = LocalizationService.GetString(ResourceKeys.ProcessingScan, "Processing...");

        bool isPreprocessed = session.IsActive && session.IsAutoTuned;
        if (bdrLookaheadStatus != null)
        {
            bdrLookaheadStatus.IsVisible = isPreprocessed;
        }

        if (isPreprocessed)
        {
            var currentPhoto = session.Activate();
            SyncUiWithScanOptions(currentPhoto.CurrentOptions);
            SetMainImage(currentPhoto.OriginalWithDetected);

            txtFileCounter.Text = LocalizationService.Format(ResourceKeys.ScanCounter, "Scan {0} of {1}", CurrentIndex + 1, ScanSessions.Count);
            lblStatus.Text = fileName;

            LoadCroppedPhotosToSlider();
            UpdatePhotoCounterLabel();
            UpdateDetectionCoverageLabel();

            if (btnResetBackground != null)
            {
                btnResetBackground.IsEnabled = currentPhoto.CustomBackgroundColorHsv != null;
            }
        }
        else
        {
            await ExecuteWithLoadingAsync($"{processingMsg} {fileName}", async ct =>
            {
                var currentPhoto = await Task.Run(() => session.Activate(), ct);

                bool shouldAutoTune = !session.IsAutoTuned && (
                    SettingsManager.Instance.Settings.AutoTuneOnScanChange ||
                    (SettingsManager.Instance.Settings.AutoAdjustOnLowCoverage && currentPhoto.TotalDetectedAreaRatio < AppConstants.LowCoverageThreshold)
                );

                if (shouldAutoTune)
                {
                    var tuneResult = await Task.Run(() => currentPhoto.AutoTune(), ct);
                    session.IsAutoTuned = true;
                    if (tuneResult.Improved || tuneResult.PhotoCount > 0)
                    {
                        session.IsModified = true;
                    }
                }
                else
                {
                    session.IsAutoTuned = true;
                }

                SyncUiWithScanOptions(currentPhoto.CurrentOptions);
                SetMainImage(currentPhoto.OriginalWithDetected);

                txtFileCounter.Text = LocalizationService.Format(ResourceKeys.ScanCounter, "Scan {0} of {1}", CurrentIndex + 1, ScanSessions.Count);
                lblStatus.Text = fileName;

                LoadCroppedPhotosToSlider();
                UpdatePhotoCounterLabel();
                UpdateDetectionCoverageLabel();

                if (btnResetBackground != null)
                {
                    btnResetBackground.IsEnabled = currentPhoto.CustomBackgroundColorHsv != null;
                }
            });
        }

        TriggerBackgroundLookahead(CurrentIndex);
    }

    private void UpdateDetectionCoverageLabel()
    {
        if (lblDetectionCoverage == null) return;
        if (ScanSessions.Count == 0 || CurrentIndex < 0 || CurrentIndex >= ScanSessions.Count)
        {
            lblDetectionCoverage.Text = "";
            return;
        }

        var session = ScanSessions[CurrentIndex];
        if (session.IsActive && session.Engine != null)
        {
            int percent = (int)Math.Round(session.Engine.TotalDetectedAreaRatio * 100.0);
            lblDetectionCoverage.Text = LocalizationService.Format(ResourceKeys.LblCoveragePercent, "Coverage: {0}%", percent);
        }
        else
        {
            lblDetectionCoverage.Text = "";
        }
    }

    private CancellationTokenSource? _lookaheadCts;
    private readonly SemaphoreSlim _lookaheadSemaphore = new(1, 1);

    private void TriggerBackgroundLookahead(int currentIndex, int lookaheadAhead = AppConstants.DefaultLookaheadAhead, int lookaheadBehind = AppConstants.DefaultLookaheadBehind)
    {
        if (ScanSessions.Count == 0 || currentIndex < 0 || currentIndex >= ScanSessions.Count) return;

        _lookaheadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _lookaheadCts = cts;
        var ct = cts.Token;

        var sessionsToProcess = new List<ScanSessionItem>();

        for (int offset = 1; offset <= lookaheadAhead; offset++)
        {
            int idx = (currentIndex + offset) % ScanSessions.Count;
            if (idx >= 0 && idx < ScanSessions.Count)
            {
                var s = ScanSessions[idx];
                if (!s.IsActive || !s.IsAutoTuned)
                {
                    sessionsToProcess.Add(s);
                }
            }
        }

        int prevIdx = (currentIndex - lookaheadBehind + ScanSessions.Count) % ScanSessions.Count;
        if (prevIdx >= 0 && prevIdx < ScanSessions.Count)
        {
            var prevS = ScanSessions[prevIdx];
            if (!prevS.IsActive || !prevS.IsAutoTuned)
            {
                sessionsToProcess.Add(prevS);
            }
        }

        for (int i = 0; i < ScanSessions.Count; i++)
        {
            if (i == currentIndex) continue;
            int dist = Math.Min(Math.Abs(i - currentIndex), ScanSessions.Count - Math.Abs(i - currentIndex));
            if (dist > lookaheadAhead + 1 && ScanSessions[i].IsActive)
            {
                ScanSessions[i].TryDeactivateIfUnmodified();
            }
        }

        if (sessionsToProcess.Count == 0) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await _lookaheadSemaphore.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                var settings = SettingsManager.Instance.Settings;
                foreach (var session in sessionsToProcess)
                {
                    if (ct.IsCancellationRequested) break;
                    session.IsProcessing = true;
                    try
                    {
                        var photo = session.Activate();
                        if (ct.IsCancellationRequested) break;

                        bool shouldAutoTune = !session.IsAutoTuned && (
                            settings.AutoTuneOnScanChange ||
                            (settings.AutoAdjustOnLowCoverage && photo.TotalDetectedAreaRatio < AppConstants.LowCoverageThreshold)
                        );

                        if (shouldAutoTune && !ct.IsCancellationRequested)
                        {
                            var tuneResult = photo.AutoTune();
                            session.IsAutoTuned = true;
                            if (tuneResult.Improved || tuneResult.PhotoCount > 0)
                            {
                                session.IsModified = true;
                            }
                        }
                        else
                        {
                            session.IsAutoTuned = true;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or InvalidOperationException)
                    {
                    }
                    finally
                    {
                        session.IsProcessing = false;
                    }
                }
            }
            finally
            {
                _lookaheadSemaphore.Release();
            }
        }, ct);
    }

    private void UpdateEmptyStateVisibility()
    {
        bool hasScans = ScanSessions.Count > 0;
        if (pnlEmptyState != null)
        {
            pnlEmptyState.IsVisible = !hasScans;
        }
        if (scrollOriginal != null)
        {
            scrollOriginal.IsVisible = hasScans;
        }
    }

    private async void BtnPrevScan_Click(object? sender, RoutedEventArgs e)
    {
        if (isLoading || !_sessionManager.HasScans) return;
        _sessionManager.MovePrevious();
        await LoadPhotosToGuiAsync();
    }

    private async void BtnNextScan_Click(object? sender, RoutedEventArgs e)
    {
        if (isLoading || !_sessionManager.HasScans) return;
        _sessionManager.MoveNext();
        await LoadPhotosToGuiAsync();
    }

    private async void BtnDeleteScan_Click(object? sender, RoutedEventArgs e)
    {
        await DeleteCurrentScanAsync();
    }

    private async Task DeleteCurrentScanAsync()
    {
        if (isLoading || !_sessionManager.HasScans) return;

        var sessionItem = _sessionManager.CurrentSession;
        if (sessionItem == null) return;

        string filePath = sessionItem.FilePath;
        string fileName = Path.GetFileName(filePath);

        _sessionManager.RemoveCurrent();

        var settings = SettingsManager.Instance.Settings;
        if (!string.IsNullOrEmpty(settings.WorkDirectory) && Directory.Exists(settings.WorkDirectory))
        {
            ProjectWorkspaceService.DeleteScan(settings.WorkDirectory, filePath, _workspaceSession);
        }
        else if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
            }
            catch (IOException)
            {
            }
        }

        string statusMsg = LocalizationService.Format(ResourceKeys.MsgScanDeleted, "Scan '{0}' deleted.", fileName);

        if (!_sessionManager.HasScans)
        {
            SetMainImage(null);
            ClearGalleryBitmaps();
            txtFileCounter.Text = LocalizationService.GetString(ResourceKeys.TxtNoFiles, "No files loaded");
            lblPhotoInfo.Text = "";
            lblStatus.Text = statusMsg;
            if (bdrLookaheadStatus != null) bdrLookaheadStatus.IsVisible = false;
            UpdateEmptyStateVisibility();
        }
        else
        {
            await LoadPhotosToGuiAsync();
            lblStatus.Text = statusMsg;
        }
    }

    private async void SldSensitivity_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (isUpdatingUiFromScan || isLoading || ScanSessions.Count == 0) return;

        var settings = SettingsManager.Instance.Settings;
        settings.BackgroundTolerance = DetectionOptions.SensitivityToTolerance(sldSensitivity.Value);
        settings.MinAreaFactor = sldMinArea.Value;
        settings.MaxAreaFactor = sldMaxArea.Value;
        settings.CannyLowThreshold = sldEdge.Value;
        SettingsManager.Instance.Save();

        var session = ScanSessions[CurrentIndex];
        session.Options.BackgroundTolerance = DetectionOptions.SensitivityToTolerance(sldSensitivity.Value);
        session.Options.MinAreaFactor = sldMinArea.Value / 100.0;
        session.Options.MaxAreaFactor = sldMaxArea.Value / 100.0;
        session.Options.CannyLowThreshold = sldEdge.Value;
        session.Options.CannyHighThreshold = sldEdge.Value * 2.5;

        await ReprocessCurrentScanAsync();
    }

    private async void BtnAutoTune_Click(object? sender, RoutedEventArgs e)
    {
        if (isLoading || ScanSessions.Count == 0) return;

        var photo = ScanSessions[CurrentIndex].Activate();
        string tuningMsg = LocalizationService.GetString(ResourceKeys.MsgAutoTuning, "Auto-tuning detection parameters...");

        await ExecuteWithLoadingAsync(tuningMsg, async ct =>
        {
            var result = await Task.Run(() => photo.AutoTune(), ct);
            ScanSessions[CurrentIndex].IsModified = true;
            SyncUiWithScanOptions(photo.CurrentOptions);
            SetMainImage(photo.OriginalWithDetected);
            LoadCroppedPhotosToSlider();
            UpdatePhotoCounterLabel();
            UpdateDetectionCoverageLabel();

            if (result.Improved || result.PhotoCount > 0)
            {
                lblStatus.Text = LocalizationService.Format(
                    ResourceKeys.MsgAutoTuneSuccess,
                    "Auto-tuned: found {0} photos (Sensitivity: {1:0}%, Edge: {2:0}).",
                    result.PhotoCount,
                    DetectionOptions.ToleranceToSensitivity(result.BestOptions.BackgroundTolerance),
                    result.BestOptions.CannyLowThreshold);
            }
            else
            {
                lblStatus.Text = LocalizationService.GetString(ResourceKeys.MsgAutoTuneFailed, "Auto-tune did not find additional photos.");
            }
        });
    }

    private async void ChkAutoOrient_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (isUpdatingUiFromScan || isLoading || ScanSessions.Count == 0) return;
        SettingsManager.Instance.Settings.AutoOrientPhotos = chkAutoOrient?.IsChecked == true;
        SettingsManager.Instance.Save();
        var session = ScanSessions[CurrentIndex];
        session.Options.AutoOrientPhotos = chkAutoOrient?.IsChecked == true;
        await ReprocessCurrentScanAsync();
    }

    private async void ChkRestoreColors_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (isUpdatingUiFromScan || isLoading || ScanSessions.Count == 0) return;
        SettingsManager.Instance.Settings.RestoreVintageColors = chkRestoreColors?.IsChecked == true;
        SettingsManager.Instance.Save();
        var session = ScanSessions[CurrentIndex];
        session.Options.RestoreVintageColors = chkRestoreColors?.IsChecked == true;
        await ReprocessCurrentScanAsync();
    }

    private async void ChkRemoveDust_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (isUpdatingUiFromScan || isLoading || ScanSessions.Count == 0) return;
        SettingsManager.Instance.Settings.RemoveDustAndScratches = chkRemoveDust?.IsChecked == true;
        SettingsManager.Instance.Save();
        var session = ScanSessions[CurrentIndex];
        session.Options.RemoveDustAndScratches = chkRemoveDust?.IsChecked == true;
        await ReprocessCurrentScanAsync();
    }

    private void ChkAutoTuneOnPass_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (isUpdatingUiFromScan || isLoading) return;
        bool isChecked = chkAutoTuneOnPass?.IsChecked == true;
        SettingsManager.Instance.Settings.AutoTuneOnScanChange = isChecked;
        SettingsManager.Instance.Save();
        if (isChecked && ScanSessions.Count > 0)
        {
            BtnAutoTune_Click(sender, e);
        }
    }

    private void MenuAutoTuneOnPass_Click(object? sender, RoutedEventArgs e)
    {
        bool newValue = !SettingsManager.Instance.Settings.AutoTuneOnScanChange;
        SettingsManager.Instance.Settings.AutoTuneOnScanChange = newValue;
        SettingsManager.Instance.Save();
        if (chkAutoTuneOnPass != null)
        {
            chkAutoTuneOnPass.IsChecked = newValue;
        }
        if (newValue && ScanSessions.Count > 0)
        {
            BtnAutoTune_Click(sender, e);
        }
    }

    private void ChkAutoAdjustOnLowCoverage_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (isUpdatingUiFromScan || isLoading) return;
        bool isChecked = chkAutoAdjustOnLowCoverage?.IsChecked == true;
        SettingsManager.Instance.Settings.AutoAdjustOnLowCoverage = isChecked;
        SettingsManager.Instance.Save();
        if (isChecked && ScanSessions.Count > 0)
        {
            var photo = ScanSessions[CurrentIndex].Activate();
            if (photo.TotalDetectedAreaRatio < AppConstants.LowCoverageThreshold)
            {
                BtnAutoTune_Click(sender, e);
            }
        }
    }

    private async Task ReprocessCurrentScanAsync()
    {
        if (isLoading || ScanSessions.Count == 0) return;

        var session = ScanSessions[CurrentIndex];
        session.IsModified = true;
        var photo = session.Activate();
        photo.ApplyOptions(GetDetectionOptionsFromUi());

        await ExecuteWithLoadingAsync(LocalizationService.GetString(ResourceKeys.MsgDetectingPhotos, "Detecting photos..."), async ct =>
        {
            await Task.Run(() => photo.DetectPhotos(), ct);
            SetMainImage(photo.OriginalWithDetected);
            LoadCroppedPhotosToSlider();
            UpdatePhotoCounterLabel();
            UpdateDetectionCoverageLabel();
            lblStatus.Text = LocalizationService.Format(ResourceKeys.MsgDetectionComplete, "Detection complete. Found {0} photos.", photo.DetectedPhotos.Count);
        });
    }

    private void SyncUiWithScanOptions(DetectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        isUpdatingUiFromScan = true;
        try
        {
            sldSensitivity.Value = DetectionOptions.ToleranceToSensitivity(options.BackgroundTolerance);
            sldMinArea.Value = options.MinAreaFactor * 100.0;
            sldMaxArea.Value = options.MaxAreaFactor * 100.0;
            sldEdge.Value = options.CannyLowThreshold;
            if (chkAutoOrient != null) chkAutoOrient.IsChecked = options.AutoOrientPhotos;
            if (chkRestoreColors != null) chkRestoreColors.IsChecked = options.RestoreVintageColors;
            if (chkRemoveDust != null) chkRemoveDust.IsChecked = options.RemoveDustAndScratches;
            if (chkAutoTuneOnPass != null) chkAutoTuneOnPass.IsChecked = SettingsManager.Instance.Settings.AutoTuneOnScanChange;
            if (chkAutoAdjustOnLowCoverage != null) chkAutoAdjustOnLowCoverage.IsChecked = SettingsManager.Instance.Settings.AutoAdjustOnLowCoverage;
        }
        finally
        {
            isUpdatingUiFromScan = false;
        }
    }

    private DetectionOptions GetDetectionOptionsFromUi()
    {
        return new DetectionOptions
        {
            BackgroundTolerance = DetectionOptions.SensitivityToTolerance(sldSensitivity.Value),
            MinAreaFactor = sldMinArea.Value / 100.0,
            MaxAreaFactor = sldMaxArea.Value / 100.0,
            CannyLowThreshold = sldEdge.Value,
            CannyHighThreshold = sldEdge.Value * 2.5,
            AutoOrientPhotos = chkAutoOrient?.IsChecked == true,
            RestoreVintageColors = chkRestoreColors?.IsChecked == true,
            RemoveDustAndScratches = chkRemoveDust?.IsChecked == true,
            BoundingBoxColor = SettingsManager.Instance.Settings.DetectionBoxColor
        };
    }
}
