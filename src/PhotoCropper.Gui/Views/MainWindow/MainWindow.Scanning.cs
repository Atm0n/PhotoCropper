using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PhotoCropper.Core.Scanning;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;
using System.Diagnostics;

namespace PhotoCropper.Gui;

internal sealed partial class MainWindow
{
    private IScannerService _scannerService;
    private readonly List<ScannerDeviceInfo> _availableScanners = [];

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"[PhotoCropper] Could not remove cancelled scan '{path}': {ex.Message}");
        }
    }

    private void ResetScannerService()
    {
        try
        {
            _scannerService.Dispose();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[PhotoCropper] Scanner service disposal failed: {ex.GetType().Name}: {ex.Message}");
        }
        _scannerService = new Naps2ScannerService();
    }

    private async void BtnScan_Click(object? sender, RoutedEventArgs e) => await ScanNewAsync();

    /// <summary>Scans from the configured scanner into the project.</summary>
    /// <returns>The staged scan files (empty if nothing was scanned).</returns>
    private async Task<IReadOnlyList<string>> ScanNewAsync()
    {
        if (isLoading) return [];

        // 1. Scans always go into a project.
        string workDir = await EnsureProjectAsync() ?? string.Empty;
        if (workDir.Length == 0) return [];
        var settings = SettingsManager.Instance.Settings;

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
            return [];
        }

        int dpi = settings.ScannerDpi > 0 ? settings.ScannerDpi : 300;
        return await ExecuteScanAsync(workDir, selectedDevice, dpi);
    }

    /// <summary>
    /// The open project's folder. Everything added (scans, imports) goes into a project, since the Crops and Finish
    /// stages write there; the start screen makes sure one is open, so this only shows it again if not.
    /// </summary>
    /// <returns>The project folder, or null when none is open.</returns>
    private Task<string?> EnsureProjectAsync()
    {
        string? workDir = _openProject;
        if (workDir == null || !Directory.Exists(workDir))
        {
            ShowStartScreen();
            return Task.FromResult<string?>(null);
        }

        ProjectWorkspaceService.InitializeWorkspace(workDir);
        _workspaceSession ??= new WorkspaceSessionState();
        return Task.FromResult<string?>(workDir);
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

    private async Task<IReadOnlyList<string>> ExecuteScanAsync(string workDir, ScannerDeviceInfo? selectedDevice, int dpi)
    {
        if (selectedDevice == null)
        {
            await ShowScannerConfigAsync();
            return [];
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
            var scannerService = _scannerService;
            await ExecuteWithLoadingAsync(scanningMsg, async (token) =>
            {
                var scannedMats = await scannerService.ScanAsync(scannerOptions, token).ConfigureAwait(false);
                try
                {
                    // A driver can return after the user cancelled or the scan timed out. The UI has
                    // already moved on by then, so discard the late result instead of staging it.
                    token.ThrowIfCancellationRequested();

                    // Staging (PNG encode + disk write) stays on the thread pool; the shared session is
                    // only updated on the UI thread once the operation has completed.
                    var paths = new List<string>(scannedMats.Count);
                    try
                    {
                        foreach (var mat in scannedMats)
                        {
                            token.ThrowIfCancellationRequested();
                            paths.Add(ProjectWorkspaceService.StageRawScanFromMat(workDir, mat));
                        }
                    }
                    catch
                    {
                        // Cancelled or failed (e.g. disk full on page 2): pages already staged would never be
                        // added to the session, so don't leave half a multi-page scan orphaned in RawScans.
                        foreach (string path in paths) TryDeleteFile(path);
                        throw;
                    }
                    stagedPaths.AddRange(paths);
                }
                finally
                {
                    foreach (var mat in scannedMats) mat.Dispose();
                }
            }, null, canCancel: true, timeout: TimeSpan.FromSeconds(dpi >= 600 ? 180 : 90), propagateExceptions: true);
        }
        catch (OperationCanceledException)
        {
            ResetScannerService();
            string cancelledMsg = LocalizationService.GetString(ResourceKeys.MsgScanCancelled, "Scanning was cancelled or timed out.");
            lblStatus.Text = cancelledMsg;
            return [];
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
            return [];
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
            return [];
        }

        if (stagedPaths.Count > 0)
        {
            _workspaceSession ??= new WorkspaceSessionState();
            foreach (string stagedPath in stagedPaths)
            {
                _workspaceSession.Scans.Add(new WorkspaceScanEntry
                {
                    RelativePath = Path.GetRelativePath(workDir, stagedPath),
                    OriginalFileName = Path.GetFileName(stagedPath),
                    StagedAtUtc = DateTime.UtcNow
                });
            }
            try
            {
                ProjectWorkspaceService.SaveSession(workDir, _workspaceSession);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The scans are already on disk in RawScans; only crash-recovery bookkeeping failed.
                Trace.TraceError($"[PhotoCropper] Failed to save session after scanning: {ex.Message}");
                ShowAppError("Operation Failed", ex.Message);
            }

            var options = _cropsVm.GetDetectionOptions();
            var newItems = stagedPaths.Select(path => new ScanSessionItem(path, options, isSaved: false, isModified: true)).ToList();
            _sessionManager.AddRange(newItems);

            CurrentIndex = _sessionManager.Count - stagedPaths.Count;
            await LoadPhotosToGuiAsync();
        }
        else
        {
            lblStatus.Text = LocalizationService.GetString(ResourceKeys.MsgNoScanData, "No image was returned by the scanner.");
        }

        return stagedPaths;
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
        catch (Exception ex)
        {
            // Scanner enumeration can fail on unsupported platforms or if the NAPS2 worker is unavailable.
            Trace.TraceWarning($"[PhotoCropper] Scanner enumeration failed: {ex.GetType().Name}: {ex.Message}");
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
