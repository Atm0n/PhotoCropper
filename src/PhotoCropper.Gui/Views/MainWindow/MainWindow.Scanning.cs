using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PhotoCropper.Core.Scanning;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Models;
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
                    Title = LocalizationService.GetString(ResourceKeys.BtnWorkDir, "Select Work Directory for Raw Scans"),
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

            if (string.IsNullOrWhiteSpace(workDir))
            {
                string picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (string.IsNullOrWhiteSpace(picturesDir))
                {
                    picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                }
                if (string.IsNullOrWhiteSpace(picturesDir))
                {
                    picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                }

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
            await RefreshScannersAsync(CancellationToken.None);
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
        await RefreshScannersAsync(CancellationToken.None);
        if (result == Dialogs.ScannerConfigResult.SaveAndScan)
        {
            BtnScan_Click(this, new RoutedEventArgs());
        }
    }

    private async Task ExecuteScanAsync(string workDir, ScannerDeviceInfo? selectedDevice, int dpi)
    {
        if (selectedDevice == null)
        {
            await ShowScannerConfigAsync();
            return;
        }

        var settings = SettingsManager.Instance.Settings;
        var scannerOptions = new ScannerOptions
        {
            Device = selectedDevice,
            Dpi = dpi,
            ColorMode = ScannerColorMode.Color,
            PageSize = ScannerPageSizeExtensions.ToScannerPageSize(settings.ScannerPageSize)
        };

        string scanningMsg = LocalizationService.Format(ResourceKeys.MsgScanning, "Scanning image from {0}...", selectedDevice.Name);

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
            }, null, canCancel: true, timeout: TimeSpan.FromSeconds(dpi >= 600 ? 180 : 90));
        }
        catch (OperationCanceledException)
        {
            ResetScannerService();
            string cancelledMsg = LocalizationService.GetString(ResourceKeys.MsgScanCancelled, "Scanning was cancelled or timed out.");
            lblStatus.Text = cancelledMsg;
            return;
        }
        catch (ScannerNotFoundException ex)
        {
            ResetScannerService();
            string notFoundTitle = LocalizationService.GetString(ResourceKeys.TitleScannerNotFound, "Scanner Not Found");
            string notFoundDetails = LocalizationService.Format(
                ResourceKeys.MsgScannerNotFoundDetails,
                "{0}\n\n• Verify your scanner is powered on and connected.\n• Make sure no other application is using the scanner.\n• Reconnect the scanner and click 'Refresh Scanners'.",
                ex.Message);

            lblStatus.Text = ex.Message;
            ShowScannerError(notFoundTitle, notFoundDetails);
            return;
        }
        catch (Exception ex)
        {
            ResetScannerService();
            string failedTemplate = LocalizationService.Format(ResourceKeys.MsgScanFailed, "Scanning failed: {0}", ex.Message);
            string errorTitle = LocalizationService.GetString(ResourceKeys.TitleScannerError, "Scanner Communication Error");
            string errorDetails = LocalizationService.Format(
                ResourceKeys.MsgScannerErrorDetails,
                "Failed to communicate with scanner '{0}':\n\n{1}\n\nTroubleshooting:\n• Check scanner power and USB/network cables.\n• Verify scanner driver status in your operating system.\n• Restart the scanner and click 'Refresh Scanners'.",
                selectedDevice.Name,
                ex.Message);

            lblStatus.Text = failedTemplate;
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
            var newItems = stagedPaths.Select(path => new ScanSessionItem(path, options, isSaved: false, isModified: true)).ToList();
            _sessionManager.AddRange(newItems);

            CurrentIndex = _sessionManager.Count - stagedPaths.Count;
            await LoadPhotosToGuiAsync();
        }
        else
        {
            lblStatus.Text = LocalizationService.GetString(ResourceKeys.MsgNoScanData, "No image was returned by the scanner.");
        }
    }

    private async Task RefreshScannersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = SettingsManager.Instance.Settings;
            bool includeNetwork = settings.IncludeNetworkScanners;
            var devices = await _scannerService.GetDevicesAsync(includeNetwork, cancellationToken).ConfigureAwait(false);
            var safeDevices = devices?.Where(d => d != null).ToList() ?? [];

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _availableScanners.Clear();
                _availableScanners.AddRange(safeDevices);
            });
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
        await RefreshScannersAsync(CancellationToken.None);
    }
}
