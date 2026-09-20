using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PhotoCropper.Core.Export;
using PhotoCropper.Core.Models;
using PhotoCropper.Core.Workspace;
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

        var imagePaths = new List<string>();
        var validExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp"
        };

        var asyncTransfer = e.DataTransfer as IAsyncDataTransfer;
        var files = asyncTransfer != null ? await asyncTransfer.TryGetFilesAsync() : null;
        if (files != null)
        {
            foreach (var file in files)
            {
                string localPath = file.Path.LocalPath;
                if (File.Exists(localPath) && validExts.Contains(Path.GetExtension(localPath)))
                {
                    imagePaths.Add(localPath);
                }
                else if (Directory.Exists(localPath))
                {
                    foreach (var ext in validExts)
                    {
                        imagePaths.AddRange(Directory.GetFiles(localPath, $"*{ext}", SearchOption.AllDirectories));
                    }
                }
            }
        }

        if (imagePaths.Count > 0)
        {
            await LoadScansFromPathsAsync(imagePaths.Distinct());
        }
    }

    private async Task LoadScansFromPathsAsync(IEnumerable<string> paths)
    {
        undoHistory.Clear();
        foreach (var session in ScanSessions)
        {
            session.Dispose();
        }
        ScanSessions.Clear();
        currentIndex = 0;

        var options = GetDetectionOptionsFromUi();
        var settings = SettingsManager.Instance.Settings;

        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            bool isAlreadyExported = ExportPathResolver.IsScanExported(
                path,
                settings.CustomOutputDirectory,
                settings.WorkDirectory);

            ScanSessions.Add(new ScanSessionItem(path, options, isSaved: isAlreadyExported, isModified: !isAlreadyExported));
        }

        UpdateEmptyStateVisibility();
        await LoadPhotosToGuiAsync();
    }

    private async void BtnOpenFiles_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;

        var fileResult = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Avalonia.Application.Current?.FindResource("BtnOpenScans")?.ToString() ?? "Select Files",
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

        string fileName = Path.GetFileName(ScanSessions[currentIndex].FilePath);
        string processingMsg = Avalonia.Application.Current?.FindResource("ProcessingScan")?.ToString() ?? "Processing...";

        await ExecuteWithLoadingAsync($"{processingMsg} {fileName}", async () =>
        {
            var session = ScanSessions[currentIndex];
            var currentPhoto = await Task.Run(() => session.Activate());
            SyncUiWithScanOptions(currentPhoto.CurrentOptions);

            img.Source = MatBitmapConverter.ToAvaloniaBitmap(currentPhoto.OriginalWithDetected);

            string scanCounterFormat = Avalonia.Application.Current?.FindResource("ScanCounter")?.ToString() ?? "Scan {0} of {1}";
            txtFileCounter.Text = string.Format(scanCounterFormat, currentIndex + 1, ScanSessions.Count);
            lblStatus.Text = fileName;

            LoadCroppedPhotosToSlider();
            UpdatePhotoCounterLabel();

            if (btnResetBackground != null)
            {
                btnResetBackground.IsEnabled = currentPhoto.CustomBackgroundColorHsv != null;
            }
        });
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
        if (isLoading || ScanSessions.Count == 0) return;
        ScanSessions[currentIndex].DeactivateIfUnmodified();
        currentIndex = (currentIndex - 1 + ScanSessions.Count) % ScanSessions.Count;
        await LoadPhotosToGuiAsync();
    }

    private async void BtnNextScan_Click(object? sender, RoutedEventArgs e)
    {
        if (isLoading || ScanSessions.Count == 0) return;
        ScanSessions[currentIndex].DeactivateIfUnmodified();
        currentIndex = (currentIndex + 1) % ScanSessions.Count;
        await LoadPhotosToGuiAsync();
    }

    private async void BtnDeleteScan_Click(object? sender, RoutedEventArgs e)
    {
        await DeleteCurrentScanAsync();
    }

    private async Task DeleteCurrentScanAsync()
    {
        if (isLoading || ScanSessions.Count == 0) return;

        var sessionItem = ScanSessions[currentIndex];
        string filePath = sessionItem.FilePath;
        string fileName = Path.GetFileName(filePath);

        sessionItem.Dispose();
        ScanSessions.RemoveAt(currentIndex);

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

        string msgTemplate = Avalonia.Application.Current?.FindResource("MsgScanDeleted")?.ToString() ?? "Scan '{0}' deleted.";
        string statusMsg = string.Format(msgTemplate, fileName);

        if (ScanSessions.Count == 0)
        {
            currentIndex = 0;
            img.Source = null;
            slides.Items.Clear();
            if (lstGallery != null) lstGallery.Items.Clear();
            txtFileCounter.Text = Avalonia.Application.Current?.FindResource("TxtNoFiles")?.ToString() ?? "No files loaded";
            lblPhotoInfo.Text = "";
            lblStatus.Text = statusMsg;
            UpdateEmptyStateVisibility();
        }
        else
        {
            if (currentIndex >= ScanSessions.Count)
            {
                currentIndex = ScanSessions.Count - 1;
            }
            await LoadPhotosToGuiAsync();
            lblStatus.Text = statusMsg;
        }
    }

    private async void SldSensitivity_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (isUpdatingUiFromScan || isLoading || ScanSessions.Count == 0) return;

        var settings = SettingsManager.Instance.Settings;
        settings.BackgroundTolerance = sldSensitivity.Value;
        settings.MinAreaFactor = sldMinArea.Value;
        settings.MaxAreaFactor = sldMaxArea.Value;
        settings.CannyLowThreshold = sldEdge.Value;
        SettingsManager.Instance.Save();

        await ReprocessCurrentScanAsync();
    }

    private async void BtnAutoTune_Click(object? sender, RoutedEventArgs e)
    {
        if (isLoading || ScanSessions.Count == 0) return;

        var photo = ScanSessions[currentIndex].Activate();
        string tuningMsg = Avalonia.Application.Current?.FindResource("MsgAutoTuning")?.ToString() ?? "Auto-tuning detection parameters...";

        await ExecuteWithLoadingAsync(tuningMsg, async () =>
        {
            var result = await Task.Run(() => photo.AutoTune());
            ScanSessions[currentIndex].IsModified = true;
            SyncUiWithScanOptions(photo.CurrentOptions);
            img.Source = MatBitmapConverter.ToAvaloniaBitmap(photo.OriginalWithDetected);
            LoadCroppedPhotosToSlider();
            UpdatePhotoCounterLabel();

            if (result.Improved || result.PhotoCount > 0)
            {
                string successFormat = Avalonia.Application.Current?.FindResource("MsgAutoTuneSuccess")?.ToString() ?? "Auto-tuned: found {0} photos (Tolerance: {1:0}, Edge: {2:0}).";
                lblStatus.Text = string.Format(successFormat, result.PhotoCount, result.BestOptions.BackgroundTolerance, result.BestOptions.CannyLowThreshold);
            }
            else
            {
                lblStatus.Text = Avalonia.Application.Current?.FindResource("MsgAutoTuneFailed")?.ToString() ?? "Auto-tune did not find additional photos.";
            }
        });
    }

    private async void ChkAutoOrient_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (isUpdatingUiFromScan || isLoading || ScanSessions.Count == 0) return;
        SettingsManager.Instance.Settings.AutoOrientPhotos = chkAutoOrient?.IsChecked == true;
        SettingsManager.Instance.Save();
        await ReprocessCurrentScanAsync();
    }

    private async void ChkRestoreColors_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (isUpdatingUiFromScan || isLoading || ScanSessions.Count == 0) return;
        SettingsManager.Instance.Settings.RestoreVintageColors = chkRestoreColors?.IsChecked == true;
        SettingsManager.Instance.Save();
        await ReprocessCurrentScanAsync();
    }

    private async void ChkRemoveDust_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (isUpdatingUiFromScan || isLoading || ScanSessions.Count == 0) return;
        SettingsManager.Instance.Settings.RemoveDustAndScratches = chkRemoveDust?.IsChecked == true;
        SettingsManager.Instance.Save();
        await ReprocessCurrentScanAsync();
    }

    private async Task ReprocessCurrentScanAsync()
    {
        if (isLoading || ScanSessions.Count == 0) return;

        var session = ScanSessions[currentIndex];
        session.IsModified = true;
        var photo = session.Activate();
        photo.ApplyOptions(GetDetectionOptionsFromUi());

        string reprocessingMsg = Avalonia.Application.Current?.FindResource("MsgReprocessing")?.ToString() ?? "Reprocessing...";
        await ExecuteWithLoadingAsync(reprocessingMsg, async () =>
        {
            await Task.Run(() => photo.DetectPhotos());
            img.Source = MatBitmapConverter.ToAvaloniaBitmap(photo.OriginalWithDetected);
            LoadCroppedPhotosToSlider();
            UpdatePhotoCounterLabel();
            string msgFormat = Avalonia.Application.Current?.FindResource("MsgDetectionComplete")?.ToString() ?? "Detection complete. Found {0} photos.";
            lblStatus.Text = string.Format(msgFormat, photo.DetectedPhotos.Count);
        });
    }

    private void SyncUiWithScanOptions(DetectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        isUpdatingUiFromScan = true;
        try
        {
            sldSensitivity.Value = options.BackgroundTolerance;
            sldMinArea.Value = options.MinAreaFactor * 100.0;
            sldMaxArea.Value = options.MaxAreaFactor * 100.0;
            sldEdge.Value = options.CannyLowThreshold;
            if (chkAutoOrient != null) chkAutoOrient.IsChecked = options.AutoOrientPhotos;
            if (chkRestoreColors != null) chkRestoreColors.IsChecked = options.RestoreVintageColors;
            if (chkRemoveDust != null) chkRemoveDust.IsChecked = options.RemoveDustAndScratches;
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
            BackgroundTolerance = sldSensitivity.Value,
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
