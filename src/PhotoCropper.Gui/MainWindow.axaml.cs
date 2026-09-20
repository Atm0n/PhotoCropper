using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PhotoCropper.Core.Models;
using PhotoCropper.Core.Scanning;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;
using System.Diagnostics.CodeAnalysis;

namespace PhotoCropper.Gui;

internal sealed record GalleryPhotoItem(Avalonia.Media.Imaging.Bitmap Image, string Label, string Dimensions, int Index);

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Avalonia Window lifecycle is managed by OnClosed override")]
internal sealed partial class MainWindow : Window
{
    private int currentIndex;
    private readonly List<ScanSessionItem> ScanSessions = [];
    private readonly UndoRedoHistory undoHistory = new();
    private bool isLoading;
    private bool isComparingRaw;
    private bool isSyncingSelection;
    private bool isUpdatingUiFromScan;
    private CancellationTokenSource? _activeOperationCts;

    public MainWindow() : this(new Naps2ScannerService())
    {
    }

    public MainWindow(IScannerService scannerService)
    {
        ArgumentNullException.ThrowIfNull(scannerService);

        _scannerService = scannerService;

        InitializeComponent();

        // Register key handlers in Tunnel phase
        AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, Window_KeyUp, RoutingStrategies.Tunnel);

        // Register Drag & Drop event handlers
        AddHandler(DragDrop.DragOverEvent, Window_DragOver);
        AddHandler(DragDrop.DropEvent, Window_Drop);

        Loaded += MainWindow_Loaded;

        PopulateLanguageMenu();
        ApplySettingsToUi();
    }

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
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            bool isAlreadyExported = CheckIfScanAlreadyExported(path);
            ScanSessions.Add(new ScanSessionItem(path, options, isSaved: isAlreadyExported, isModified: !isAlreadyExported));
        }
        await LoadPhotosToGuiAsync();
    }

    private static bool CheckIfScanAlreadyExported(string scanFilePath)
    {
        var settings = SettingsManager.Instance.Settings;
        string? targetOutputFolder = settings.CustomOutputDirectory;
        if (string.IsNullOrEmpty(targetOutputFolder) && !string.IsNullOrEmpty(settings.WorkDirectory) && Directory.Exists(settings.WorkDirectory))
        {
            targetOutputFolder = ProjectWorkspaceService.GetCroppedDirectory(settings.WorkDirectory);
        }
        else if (string.IsNullOrEmpty(targetOutputFolder))
        {
            string? directory = Path.GetDirectoryName(scanFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                string folderName = Path.GetFileName(directory);
                if (string.Equals(folderName, ProjectWorkspaceService.RawScansFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    string? parentDir = Path.GetDirectoryName(directory);
                    targetOutputFolder = !string.IsNullOrEmpty(parentDir)
                        ? Path.Combine(parentDir, ProjectWorkspaceService.CroppedFolderName)
                        : Path.Combine(directory, "cropped");
                }
                else
                {
                    targetOutputFolder = Path.Combine(directory, "cropped");
                }
            }
        }

        if (string.IsNullOrEmpty(targetOutputFolder) || !Directory.Exists(targetOutputFolder))
        {
            return false;
        }

        string baseName = Path.GetFileNameWithoutExtension(scanFilePath);
        return Directory.EnumerateFiles(targetOutputFolder, $"{baseName}_*.*").Any();
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

    private void ApplySettingsToUi()
    {
        var settings = SettingsManager.Instance.Settings;
        sldSensitivity.Value = settings.BackgroundTolerance;
        sldZoom.Value = settings.ZoomLevel;
        sldMinArea.Value = settings.MinAreaFactor;
        sldMaxArea.Value = settings.MaxAreaFactor;
        sldEdge.Value = settings.CannyLowThreshold;
        tglAdvanced.IsChecked = settings.AdvancedVisible;
        if (chkAutoOrient != null)
        {
            chkAutoOrient.IsChecked = settings.AutoOrientPhotos;
        }
        if (chkRestoreColors != null)
        {
            chkRestoreColors.IsChecked = settings.RestoreVintageColors;
        }
        if (chkRemoveDust != null)
        {
            chkRemoveDust.IsChecked = settings.RemoveDustAndScratches;
        }

        if (cbFormat != null)
        {
            cbFormat.SelectedIndex = string.Equals(settings.PreferredFormat, "PNG", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }
        if (sldJpegQuality != null)
        {
            sldJpegQuality.Value = settings.JpegQuality;
        }
        if (txtOutputDir != null)
        {
            txtOutputDir.Text = settings.CustomOutputDirectory ?? "";
        }
        if (pnlJpegQuality != null)
        {
            pnlJpegQuality.IsVisible = !string.Equals(settings.PreferredFormat, "PNG", StringComparison.OrdinalIgnoreCase);
        }

        UpdateWorkspaceUi(settings.WorkDirectory);

        if (cbScannerDpi != null)
        {
            cbScannerDpi.SelectedIndex = settings.ScannerDpi switch
            {
                150 => 0,
                600 => 2,
                _ => 1
            };
        }

        if (chkNetworkScanners != null)
        {
            chkNetworkScanners.IsChecked = settings.IncludeNetworkScanners;
        }
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
            RemoveDustAndScratches = chkRemoveDust?.IsChecked == true
        };
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

    private void PopulateLanguageMenu()
    {
        var menus = new[] { menuLanguage, menuAltLanguage };
        var languages = LocalizationManager.GetAvailableLanguages();

        foreach (var menu in menus)
        {
            if (menu == null) continue;
            menu.Items.Clear();

            foreach (var lang in languages)
            {
                var item = new MenuItem
                {
                    Header = lang.Name,
                    Tag = lang.Code,
                    Focusable = false,
                    IsTabStop = false
                };
                item.Click += (s, _) =>
                {
                    if (s is MenuItem mi && mi.Tag is string code)
                    {
                        LocalizationManager.SetLanguage(code);
                        UpdateWorkspaceUi(SettingsManager.Instance.Settings.WorkDirectory);
                        UpdateSelectionUi();
                        FocusManager?.Focus(null);
                    }
                };
                menu.Items.Add(item);
            }
        }
    }

    private async Task ExecuteWithLoadingAsync(
        string statusText,
        Func<CancellationToken, Task> action,
        string? completionText = null,
        bool canCancel = false,
        TimeSpan? timeout = null)
    {
        if (isLoading) return;
        isLoading = true;
        pnlLoadingOverlay.IsVisible = true;
        btnCancelLoading.IsVisible = canCancel;
        btnCancelLoading.IsEnabled = true;
        txtLoadingText.Text = statusText;
        lblStatus.Text = statusText;

        using var cts = timeout.HasValue
            ? new CancellationTokenSource(timeout.Value)
            : new CancellationTokenSource();
        _activeOperationCts = cts;

        var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancelReg = cts.Token.Register(() => cancelTcs.TrySetResult(true));

        try
        {
            var actionTask = action(cts.Token);
            var completedTask = await Task.WhenAny(actionTask, cancelTcs.Task).ConfigureAwait(true);

            if (completedTask == cancelTcs.Task)
            {
                throw new OperationCanceledException(cts.Token);
            }

            await actionTask.ConfigureAwait(true);

            if (completionText != null)
            {
                lblStatus.Text = completionText;
            }
        }
        finally
        {
            _activeOperationCts = null;
            btnCancelLoading.IsVisible = false;
            pnlLoadingOverlay.IsVisible = false;
            isLoading = false;
        }
    }

    private Task ExecuteWithLoadingAsync(string statusText, Func<Task> action, string? completionText = null)
    {
        return ExecuteWithLoadingAsync(statusText, _ => action(), completionText, canCancel: false);
    }

    private void BtnCancelLoading_Click(object? sender, RoutedEventArgs e)
    {
        btnCancelLoading.IsEnabled = false;
        txtLoadingText.Text = Avalonia.Application.Current?.FindResource("MsgScanCancelled")?.ToString() ?? "Cancelling...";
        _activeOperationCts?.Cancel();
    }

    private async Task LoadPhotosToGuiAsync()
    {
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

    private async void BtnPrevScan_Click(object? sender, RoutedEventArgs e)
    {
        if (isLoading || ScanSessions.Count == 0) return;
        ScanSessions[currentIndex].Deactivate();
        currentIndex = (currentIndex - 1 + ScanSessions.Count) % ScanSessions.Count;
        await LoadPhotosToGuiAsync();
    }

    private async void BtnNextScan_Click(object? sender, RoutedEventArgs e)
    {
        if (isLoading || ScanSessions.Count == 0) return;
        ScanSessions[currentIndex].Deactivate();
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
            txtFileCounter.Text = Avalonia.Application.Current?.FindResource("TxtNoFiles")?.ToString() ?? "No files loaded";
            lblPhotoInfo.Text = "";
            lblStatus.Text = statusMsg;
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

    private async void BtnSaveImages_Click(object? sender, RoutedEventArgs e)
    {
        if (isLoading || ScanSessions.Count == 0) return;

        var pendingSessions = ScanSessions.Where(s => !s.IsSaved || s.IsModified).ToList();
        if (pendingSessions.Count == 0)
        {
            string alreadySavedMsg = Avalonia.Application.Current?.FindResource("MsgAllScansAlreadySaved")?.ToString()
                ?? "All {0} scans are already saved to 'Cropped'. No changes to export.";
            lblStatus.Text = string.Format(alreadySavedMsg, ScanSessions.Count);
            return;
        }

        var settings = SettingsManager.Instance.Settings;
        int totalScans = pendingSessions.Count;
        int completedScans = 0;
        int totalSavedPhotos = 0;

        string savingMsg = Avalonia.Application.Current?.FindResource("MsgSavingProgress")?.ToString() ?? "Exporting scan {0} of {1} ({2} photos saved)...";
        string msgFormat = Avalonia.Application.Current?.FindResource("MsgSaved")?.ToString() ?? "Successfully saved {0} photos to 'cropped' folders.";

        string? targetOutputFolder = settings.CustomOutputDirectory;
        if (string.IsNullOrEmpty(targetOutputFolder) && !string.IsNullOrEmpty(settings.WorkDirectory) && Directory.Exists(settings.WorkDirectory))
        {
            targetOutputFolder = ProjectWorkspaceService.GetCroppedDirectory(settings.WorkDirectory);
        }

        int maxConcurrency = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

        await ExecuteWithLoadingAsync(string.Format(savingMsg, 1, totalScans, 0), async () =>
        {
            await Task.Run(() =>
            {
                Parallel.ForEach(pendingSessions, new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency }, (session) =>
                {
                    bool wasActive = session.IsActive;
                    var engine = session.Activate();
                    try
                    {
                        engine.SaveDetectedPhotos(
                            targetOutputFolder,
                            settings.PreferredFormat,
                            settings.JpegQuality);

                        int savedCount = engine.DetectedPhotos.Count;
                        Interlocked.Add(ref totalSavedPhotos, savedCount);

                        session.IsSaved = true;
                        session.IsModified = false;

                        if (_workspaceSession != null)
                        {
                            var entry = _workspaceSession.Scans.FirstOrDefault(s =>
                                string.Equals(s.RelativePath, session.FilePath, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(Path.GetFileName(s.RelativePath), Path.GetFileName(session.FilePath), StringComparison.OrdinalIgnoreCase));
                            if (entry != null)
                            {
                                entry.IsProcessed = true;
                                entry.ExtractedPhotoCount = savedCount;
                            }
                        }
                    }
                    finally
                    {
                        if (!wasActive)
                        {
                            session.Deactivate();
                        }
                    }

                    int done = Interlocked.Increment(ref completedScans);
                    if (done % 15 == 0)
                    {
                        GC.Collect(1, GCCollectionMode.Optimized, false);
                    }

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        lblStatus.Text = string.Format(savingMsg, done, totalScans, totalSavedPhotos);
                    });
                });

                if (_workspaceSession != null && !string.IsNullOrEmpty(settings.WorkDirectory) && Directory.Exists(settings.WorkDirectory))
                {
                    ProjectWorkspaceService.SaveSession(settings.WorkDirectory, _workspaceSession);
                }
            });

            PhotoCropper.Core.Utils.NotificationSound.PlayCompletionSound();
        }, string.Format(msgFormat, totalSavedPhotos));
    }

    private void ToggleMenuBar()
    {
        if (pnlMenuBar != null)
        {
            pnlMenuBar.IsVisible = !pnlMenuBar.IsVisible;
        }
    }

    private void MenuExit_Click(object? sender, RoutedEventArgs e) => Close();
    private void MenuUndo_Click(object? sender, RoutedEventArgs e) => PerformUndo();
    private void MenuRedo_Click(object? sender, RoutedEventArgs e) => PerformRedo();
    private void MenuViewCarousel_Click(object? sender, RoutedEventArgs e) => SetViewMode(false);
    private void MenuViewGrid_Click(object? sender, RoutedEventArgs e) => SetViewMode(true);
    private void BtnToggleAdvanced_Click(object? sender, RoutedEventArgs e)
    {
        if (tglAdvanced != null)
        {
            tglAdvanced.IsChecked = !tglAdvanced.IsChecked;
        }
    }
    private void BtnReset_Click(object? sender, RoutedEventArgs e) => BtnResetDefaults_Click(sender, e);

    private void BtnHelp_Click(object? sender, RoutedEventArgs e) => pnlHelpOverlay.IsVisible = true;
    private void BtnCloseHelp_Click(object? sender, RoutedEventArgs e) => pnlHelpOverlay.IsVisible = false;

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

    private void CbFormat_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (cbFormat == null || pnlJpegQuality == null) return;

        bool isJpeg = cbFormat.SelectedIndex == 0;
        pnlJpegQuality.IsVisible = isJpeg;

        var settings = SettingsManager.Instance.Settings;
        settings.PreferredFormat = isJpeg ? "JPEG" : "PNG";
        SettingsManager.Instance.Save();
    }

    private void SldJpegQuality_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (sldJpegQuality == null) return;
        SettingsManager.Instance.Settings.JpegQuality = (int)sldJpegQuality.Value;
        SettingsManager.Instance.Save();
    }

    private async void BtnBrowseDir_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;

        var folderResult = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Avalonia.Application.Current?.FindResource("LblOutputDir")?.ToString() ?? "Select Export Folder",
            AllowMultiple = false
        });

        if (folderResult != null && folderResult.Count > 0)
        {
            var path = folderResult[0].Path.LocalPath;
            txtOutputDir.Text = path;
            SettingsManager.Instance.Settings.CustomOutputDirectory = path;
            SettingsManager.Instance.Save();
        }
    }

    private void BtnClearOutputDir_Click(object? sender, RoutedEventArgs e)
    {
        if (txtOutputDir == null) return;
        txtOutputDir.Text = "";
        SettingsManager.Instance.Settings.CustomOutputDirectory = null;
        SettingsManager.Instance.Save();
    }

    private async void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (isLoading)
        {
            e.Handled = true;
            return;
        }

        // Toggle hidden menu bar with Alt key or F10
        if (e.Key == Key.LeftAlt || e.Key == Key.RightAlt || e.Key == Key.F10)
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                ToggleMenuBar();
                e.Handled = true;
                return;
            }
        }

        if (pnlMenuBar != null && pnlMenuBar.IsVisible && e.Key == Key.Escape)
        {
            pnlMenuBar.IsVisible = false;
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.S)
            {
                BtnSaveImages_Click(null, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
            if (e.Key == Key.O)
            {
                BtnOpenFiles_Click(null, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Z)
            {
                PerformUndo();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Y)
            {
                PerformRedo();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.A && rbViewGrid?.IsChecked == true)
            {
                SelectAllGalleryPhotos();
                e.Handled = true;
                return;
            }
        }

        if (pnlScannerOverlay.IsVisible && e.Key == Key.Escape)
        {
            pnlScannerOverlay.IsVisible = false;
            e.Handled = true;
            return;
        }

        if (pnlErrorOverlay.IsVisible && e.Key == Key.Escape)
        {
            pnlErrorOverlay.IsVisible = false;
            e.Handled = true;
            return;
        }

        if (pnlLoadingOverlay.IsVisible && btnCancelLoading.IsVisible && e.Key == Key.Escape)
        {
            btnCancelLoading.IsEnabled = false;
            txtLoadingText.Text = Avalonia.Application.Current?.FindResource("MsgScanCancelled")?.ToString() ?? "Cancelling...";
            _activeOperationCts?.Cancel();
            e.Handled = true;
            return;
        }

        if (pnlHelpOverlay.IsVisible && e.Key == Key.Escape)
        {
            pnlHelpOverlay.IsVisible = false;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F1)
        {
            pnlHelpOverlay.IsVisible = true;
            e.Handled = true;
            return;
        }

        if (rbViewGrid?.IsChecked == true && lstGallery?.SelectedItems != null && lstGallery.SelectedItems.Count > 1 && e.Key == Key.Escape)
        {
            ClearGallerySelection();
            e.Handled = true;
            return;
        }

        if (isRefining)
        {
            switch (e.Key)
            {
                case Key.Enter:
                case Key.A:
                    AcceptRefine();
                    e.Handled = true;
                    break;
                case Key.Back:
                case Key.Escape:
                case Key.C:
                    RejectRefine();
                    e.Handled = true;
                    break;
            }
            return;
        }

        if (e.Key == Key.F5)
        {
            BtnScan_Click(null, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (ScanSessions.Count == 0) return;

        if (e.Key == Key.Delete && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            await DeleteCurrentScanAsync();
            e.Handled = true;
            return;
        }

        var key = e.Key;
        if (key == Key.OemPlus) key = Key.Add;
        if (key == Key.OemMinus) key = Key.Subtract;
        if (key == Key.OemTilde || key == Key.Oem3) key = Key.N;

        switch (key)
        {
            case Key.Up:
            case Key.PageUp:
                FocusManager?.Focus(null);
                ScanSessions[currentIndex].Deactivate();
                currentIndex = (currentIndex - 1 + ScanSessions.Count) % ScanSessions.Count;
                await LoadPhotosToGuiAsync();
                e.Handled = true;
                break;

            case Key.Down:
            case Key.PageDown:
                FocusManager?.Focus(null);
                ScanSessions[currentIndex].Deactivate();
                currentIndex = (currentIndex + 1) % ScanSessions.Count;
                await LoadPhotosToGuiAsync();
                e.Handled = true;
                break;

            case Key.Left:
                FocusManager?.Focus(null);
                if (slides != null) slides.Previous();
                e.Handled = true;
                break;

            case Key.Right:
                FocusManager?.Focus(null);
                if (slides != null) slides.Next();
                e.Handled = true;
                break;

            case Key.R:
                FocusManager?.Focus(null);
                await RotateSelectedPhotosAsync();
                e.Handled = true;
                break;

            case Key.X:
            case Key.Delete:
                FocusManager?.Focus(null);
                DeleteSelectedPhotos();
                e.Handled = true;
                break;

            case Key.Space:
            case Key.B:
                if (!isComparingRaw && ScanSessions.Count > 0 && slides != null && slides.SelectedIndex >= 0)
                {
                    int sel = slides.SelectedIndex;
                    var engine = ScanSessions[currentIndex].Activate();
                    if (sel < engine.RawDetectedPhotos.Count)
                    {
                        isComparingRaw = true;
                        slides.Items[sel] = MatBitmapConverter.ToAvaloniaBitmap(engine.RawDetectedPhotos[sel]);
                        slides.SelectedIndex = sel;
                    }
                }
                e.Handled = true;
                break;
        }
    }

    private void Window_KeyUp(object? sender, KeyEventArgs e)
    {
        if (isComparingRaw && (e.Key == Key.Space || e.Key == Key.B))
        {
            isComparingRaw = false;
            if (ScanSessions.Count > 0 && slides != null && slides.SelectedIndex >= 0)
            {
                int sel = slides.SelectedIndex;
                var engine = ScanSessions[currentIndex].Activate();
                if (sel < engine.DetectedPhotos.Count)
                {
                    slides.Items[sel] = MatBitmapConverter.ToAvaloniaBitmap(engine.DetectedPhotos[sel]);
                    slides.SelectedIndex = sel;
                }
            }
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        var settings = SettingsManager.Instance.Settings;
        settings.BackgroundTolerance = sldSensitivity.Value;
        settings.ZoomLevel = sldZoom.Value;
        settings.MinAreaFactor = sldMinArea.Value;
        settings.MaxAreaFactor = sldMaxArea.Value;
        settings.CannyLowThreshold = sldEdge.Value;
        settings.AdvancedVisible = tglAdvanced.IsChecked ?? false;

        if (cbFormat != null)
        {
            settings.PreferredFormat = cbFormat.SelectedIndex == 1 ? "PNG" : "JPEG";
        }
        if (sldJpegQuality != null)
        {
            settings.JpegQuality = (int)sldJpegQuality.Value;
        }
        if (txtOutputDir != null)
        {
            settings.CustomOutputDirectory = string.IsNullOrEmpty(txtOutputDir.Text) ? null : txtOutputDir.Text;
        }

        if (!string.IsNullOrEmpty(settings.WorkDirectory) && Directory.Exists(settings.WorkDirectory) && _workspaceSession != null)
        {
            ProjectWorkspaceService.SaveSession(settings.WorkDirectory, _workspaceSession);
        }

        SettingsManager.Instance.Save();

        base.OnClosed(e);
        _scannerService.Dispose();
        undoHistory.Dispose();
        foreach (var session in ScanSessions)
        {
            session.Dispose();
        }
        ScanSessions.Clear();
    }
}
