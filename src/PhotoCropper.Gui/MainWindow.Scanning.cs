using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PhotoCropper.Core.Scanning;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;

namespace PhotoCropper.Gui;

internal sealed partial class MainWindow
{
    private IScannerService _scannerService;
    private readonly List<ScannerDeviceInfo> _availableScanners = [];

    private void ResetScannerService()
    {
        try
        {
            _scannerService.Dispose();
        }
        catch
        {
        }
        _scannerService = new Naps2ScannerService();
    }

    private async void BtnScan_Click(object? sender, RoutedEventArgs e)
    {
        if (isLoading) return;

        // 1. Ensure Work Directory
        var settings = SettingsManager.Instance.Settings;
        string? workDir = settings.WorkDirectory;
        if (string.IsNullOrEmpty(workDir) || !Directory.Exists(workDir))
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider != null)
            {
                var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = Avalonia.Application.Current?.FindResource("BtnWorkDir")?.ToString() ?? "Select Work Directory for Raw Scans",
                    AllowMultiple = false
                });

                if (folders.Count > 0)
                {
                    workDir = folders[0].Path.LocalPath;
                    settings.WorkDirectory = workDir;
                    SettingsManager.Instance.Save();
                    UpdateWorkspaceUi(workDir);
                }
            }

            if (string.IsNullOrEmpty(workDir))
            {
                string picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (string.IsNullOrEmpty(picturesDir)) picturesDir = Path.GetTempPath();
                workDir = Path.Combine(picturesDir, "PhotoCropper_Workspace");
                settings.WorkDirectory = workDir;
                SettingsManager.Instance.Save();
                UpdateWorkspaceUi(workDir);
            }
        }

        ProjectWorkspaceService.InitializeWorkspace(workDir);

        // 2. Discover scanner if needed
        if (_availableScanners.Count == 0)
        {
            await RefreshScannersAsync();
        }

        // 3. If no scanner has been configured yet or no scanner is detected, show configuration modal
        var selectedDevice = _availableScanners.FirstOrDefault(s => s.Id == settings.SelectedScannerId);
        if (selectedDevice == null || string.IsNullOrEmpty(settings.SelectedScannerId) || _availableScanners.Count == 0)
        {
            await ShowScannerConfigAsync();
            return;
        }

        int dpi = settings.ScannerDpi > 0 ? settings.ScannerDpi : 300;
        await ExecuteScanAsync(workDir, selectedDevice, dpi);
    }

    private async void BtnScannerConfig_Click(object? sender, RoutedEventArgs e)
    {
        await ShowScannerConfigAsync();
    }

    private async Task ShowScannerConfigAsync()
    {
        using var dialog = new Dialogs.ScannerConfigDialog(_scannerService);
        var result = await dialog.ShowDialog<Dialogs.ScannerConfigResult>(this);
        await RefreshScannersAsync();
        if (result == Dialogs.ScannerConfigResult.SaveAndScan)
        {
            BtnScan_Click(this, new RoutedEventArgs());
        }
    }

    private async Task ExecuteScanAsync(string workDir, ScannerDeviceInfo selectedDevice, int dpi)
    {
        var scannerOptions = new ScannerOptions
        {
            Device = selectedDevice,
            Dpi = dpi,
            ColorMode = ScannerColorMode.Color
        };

        string scanningMsg = string.Format(
            Avalonia.Application.Current?.FindResource("MsgScanning")?.ToString() ?? "Scanning image from {0}...",
            selectedDevice.Name);

        var stagedPaths = new List<string>();
        try
        {
            btnScan.IsEnabled = false;
            await ExecuteWithLoadingAsync(scanningMsg, async (token) =>
            {
                var scannedMats = await _scannerService.ScanAsync(scannerOptions, token).ConfigureAwait(false);
                if (scannedMats.Count == 0) return;

                foreach (var mat in scannedMats)
                {
                    using (mat)
                    {
                        string stagedPath = ProjectWorkspaceService.StageRawScanFromMat(workDir, mat);
                        stagedPaths.Add(stagedPath);

                        string relativePath = Path.GetRelativePath(workDir, stagedPath);
                        _workspaceSession ??= new WorkspaceSessionState();
                        _workspaceSession.Scans.Add(new WorkspaceScanEntry
                        {
                            RelativePath = relativePath,
                            OriginalFileName = Path.GetFileName(stagedPath),
                            StagedAtUtc = DateTime.UtcNow
                        });
                    }
                }

                if (_workspaceSession != null)
                {
                    ProjectWorkspaceService.SaveSession(workDir, _workspaceSession);
                }
            }, null, canCancel: true, timeout: TimeSpan.FromSeconds(45));
        }
        catch (OperationCanceledException)
        {
            ResetScannerService();
            string cancelledMsg = Avalonia.Application.Current?.FindResource("MsgScanCancelled")?.ToString() ?? "Scanning was cancelled or timed out.";
            lblStatus.Text = cancelledMsg;
            return;
        }
        catch (ScannerNotFoundException ex)
        {
            ResetScannerService();
            string notFoundTitle = Avalonia.Application.Current?.FindResource("TitleScannerNotFound")?.ToString() ?? "Scanner Not Found";
            string notFoundTemplate = Avalonia.Application.Current?.FindResource("MsgScannerNotFoundDetails")?.ToString() ??
                "{0}\n\n• Verify your scanner is powered on and connected.\n• Make sure no other application is using the scanner.\n• Reconnect the scanner and click 'Refresh Scanners'.";
            string notFoundDetails = string.Format(notFoundTemplate, ex.Message);

            lblStatus.Text = ex.Message;
            ShowScannerError(notFoundTitle, notFoundDetails);
            return;
        }
        catch (Exception ex)
        {
            ResetScannerService();
            string failedTemplate = Avalonia.Application.Current?.FindResource("MsgScanFailed")?.ToString() ?? "Scanning failed: {0}";
            string errorTitle = Avalonia.Application.Current?.FindResource("TitleScannerError")?.ToString() ?? "Scanner Communication Error";
            string errorTemplate = Avalonia.Application.Current?.FindResource("MsgScannerErrorDetails")?.ToString() ??
                "Failed to communicate with scanner '{0}':\n\n{1}\n\nTroubleshooting:\n• Check scanner power and USB/network cables.\n• Verify scanner driver status in your operating system.\n• Restart the scanner and click 'Refresh Scanners'.";
            string errorDetails = string.Format(errorTemplate, selectedDevice.Name, ex.Message);

            lblStatus.Text = string.Format(failedTemplate, ex.Message);
            ShowScannerError(errorTitle, errorDetails);
            return;
        }
        finally
        {
            btnScan.IsEnabled = true;
        }

        if (stagedPaths.Count > 0)
        {
            var options = GetDetectionOptionsFromUi();
            foreach (var path in stagedPaths)
            {
                ScanSessions.Add(new ScanSessionItem(path, options, isSaved: false, isModified: true));
            }

            currentIndex = ScanSessions.Count - stagedPaths.Count;
            await LoadPhotosToGuiAsync();
        }
    }

    private async Task RefreshScannersAsync()
    {
        try
        {
            var settings = SettingsManager.Instance.Settings;
            bool includeNetwork = settings.IncludeNetworkScanners;
            var devices = await _scannerService.GetDevicesAsync(includeNetwork).ConfigureAwait(false);
            _availableScanners.Clear();
            _availableScanners.AddRange(devices);
        }
        catch
        {
            // Scanner enumeration failed or unsupported platform
        }
    }

    private void ShowScannerError(string title, string message) => ShowAppError(title, message);

    private void BtnCloseError_Click(object? sender, RoutedEventArgs e)
    {
        pnlErrorOverlay.IsVisible = false;
    }

    private async void BtnErrorRefresh_Click(object? sender, RoutedEventArgs e)
    {
        pnlErrorOverlay.IsVisible = false;
        await RefreshScannersAsync();
    }
}
