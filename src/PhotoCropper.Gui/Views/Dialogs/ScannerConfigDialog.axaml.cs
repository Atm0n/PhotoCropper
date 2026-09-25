using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using PhotoCropper.Core.Scanning;
using PhotoCropper.Gui.Services;

namespace PhotoCropper.Gui.Dialogs;

internal enum ScannerConfigResult
{
    Cancel,
    Save,
    SaveAndScan
}

internal sealed partial class ScannerConfigDialog : Window, IDisposable
{
    private readonly IScannerService _scannerService;
    private readonly bool _ownsService;
    private readonly List<ScannerDeviceInfo> _availableScanners = [];
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private bool _isInitializing = true;
    private bool _disposed;

    public ScannerConfigDialog() : this(new Naps2ScannerService(), ownsService: true)
    {
    }

    public ScannerConfigDialog(IScannerService scannerService) : this(scannerService, ownsService: false)
    {
    }

    private ScannerConfigDialog(IScannerService scannerService, bool ownsService)
    {
        ArgumentNullException.ThrowIfNull(scannerService);
        _scannerService = scannerService;
        _ownsService = ownsService;

        InitializeComponent();
        KeyDown += ScannerConfigDialog_KeyDown;
        Loaded += ScannerConfigDialog_Loaded;
        Closed += (_, _) => Dispose();
    }

    private void ScannerConfigDialog_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(ScannerConfigResult.Cancel);
            e.Handled = true;
        }
    }

    private async void ScannerConfigDialog_Loaded(object? sender, RoutedEventArgs e)
    {
        _isInitializing = true;
        try
        {
            var settings = SettingsManager.Instance.Settings;

            if (chkNetworkScanners != null)
            {
                chkNetworkScanners.IsChecked = settings.IncludeNetworkScanners;
            }

            if (cbScannerDpi != null)
            {
                cbScannerDpi.SelectedIndex = settings.ScannerDpi switch
                {
                    150 => 0,
                    600 => 2,
                    _ => 1
                };
            }

            if (cbScannerPageSize != null)
            {
                var pageSize = ScannerPageSizeExtensions.ToScannerPageSize(settings.ScannerPageSize);
                cbScannerPageSize.SelectedIndex = pageSize switch
                {
                    ScannerPageSize.A4 => 1,
                    ScannerPageSize.Letter => 2,
                    ScannerPageSize.Legal => 3,
                    ScannerPageSize.B5 => 4,
                    ScannerPageSize.A5 => 5,
                    _ => 0
                };
            }
        }
        finally
        {
            _isInitializing = false;
        }

        await RefreshScannersAsync(_cts.Token);
    }

    private async Task RefreshScannersAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refreshLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (btnRefreshScanners != null) btnRefreshScanners.IsEnabled = false;
            if (btnSaveAndScan != null) btnSaveAndScan.IsEnabled = false;
            if (txtScannerStatus != null)
            {
                txtScannerStatus.Text = LocalizationService.GetString(ResourceKeys.TxtScanningSearching, "Searching for connected scanners...");
                txtScannerStatus.Foreground = LocalizationService.TryGetResource<IBrush>(ResourceKeys.AppAccentBrush, out var accentBrush) && accentBrush != null ? accentBrush : Brush.Parse("#3399ff");
            }
        });

        try
        {
            var settings = SettingsManager.Instance.Settings;
            bool includeNetwork = settings.IncludeNetworkScanners ||
                                  settings.SelectedScannerId?.StartsWith("ESCL:", StringComparison.OrdinalIgnoreCase) == true;
            var devices = await _scannerService.GetDevicesAsync(includeNetwork, cancellationToken).ConfigureAwait(false);
            var safeDevices = devices?.Where(d => d != null).ToList() ?? [];

            if (_disposed) return;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed) return;

                _availableScanners.Clear();
                _availableScanners.AddRange(safeDevices);

                if (cbScanner != null)
                {
                    cbScanner.ItemsSource = _availableScanners.Where(d => d != null).Select(d => d.ToString()).ToList();

                    int selectedIdx = _availableScanners.FindIndex(d => d != null && d.Id == settings.SelectedScannerId);
                    if (selectedIdx >= 0)
                    {
                        cbScanner.SelectedIndex = selectedIdx;
                    }
                    else if (_availableScanners.Count > 0)
                    {
                        cbScanner.SelectedIndex = 0;
                    }
                    else
                    {
                        cbScanner.SelectedIndex = -1;
                    }
                }

                UpdateStatusLabel();

                if (cbScanner != null && cbScanner.SelectedIndex >= 0 && cbScanner.SelectedIndex < _availableScanners.Count)
                {
                    _ = UpdateDetectedBedDimensionsAsync(_availableScanners[cbScanner.SelectedIndex], cancellationToken);
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Dialog closed or refresh cancelled
        }
        catch
        {
            if (!_disposed)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(UpdateStatusLabel);
            }
        }
        finally
        {
            if (!_disposed)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (btnRefreshScanners != null) btnRefreshScanners.IsEnabled = true;
                });
                _refreshLock.Release();
            }
        }
    }

    private void UpdateStatusLabel()
    {
        if (txtScannerStatus == null) return;

        if (_availableScanners.Count == 0)
        {
            txtScannerStatus.Text = LocalizationService.GetString(ResourceKeys.MsgNoScannerFound, "No scanner detected. Click 🔄 to refresh.");
            txtScannerStatus.Foreground = LocalizationService.TryGetResource<IBrush>(ResourceKeys.AppDangerTextBrush, out var dangerBrush) && dangerBrush != null ? dangerBrush : Brush.Parse("#ffaa44");
            if (cbScanner != null)
            {
                cbScanner.SelectedIndex = -1;
            }
        }
        else
        {
            txtScannerStatus.Text = $"{_availableScanners.Count} scanner(s) found.";
            txtScannerStatus.Foreground = LocalizationService.TryGetResource<IBrush>(ResourceKeys.AppSuccessTextBrush, out var successBrush) && successBrush != null ? successBrush : Brush.Parse("#44cc66");
        }

        if (btnSaveAndScan != null)
        {
            btnSaveAndScan.IsEnabled = _availableScanners.Count > 0;
        }
    }

    private async void CbScanner_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (cbScanner != null && cbScanner.SelectedIndex >= 0 && cbScanner.SelectedIndex < _availableScanners.Count)
        {
            var selected = _availableScanners[cbScanner.SelectedIndex];
            if (selected != null)
            {
                SettingsManager.Instance.Settings.SelectedScannerId = selected.Id;
                await UpdateDetectedBedDimensionsAsync(selected, _cts.Token);
            }
        }
    }

    private async Task UpdateDetectedBedDimensionsAsync(ScannerDeviceInfo? selectedDevice, CancellationToken cancellationToken = default)
    {
        if (cbiPageSizeAuto == null) return;
        if (selectedDevice == null)
        {
            cbiPageSizeAuto.Content = "Auto / Full Bed (Auto-Detect Maximum)";
            return;
        }

        try
        {
            var dims = await _scannerService.GetDeviceBedDimensionsAsync(selectedDevice, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(dims) && !_disposed)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (cbiPageSizeAuto != null)
                    {
                        cbiPageSizeAuto.Content = $"Auto / Full Bed (Detected: {dims})";
                    }
                });
            }
        }
        catch
        {
            // Leave default auto label if querying dimensions fails
        }
    }

    private void CbScannerDpi_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (cbScannerDpi?.SelectedItem is ComboBoxItem item)
        {
            string? content = item.Content?.ToString();
            int dpi = 300;
            if (content != null)
            {
                if (content.StartsWith("150", StringComparison.Ordinal)) dpi = 150;
                else if (content.StartsWith("600", StringComparison.Ordinal)) dpi = 600;
                else if (int.TryParse(content, out int parsed)) dpi = parsed;
            }
            SettingsManager.Instance.Settings.ScannerDpi = dpi;
        }
    }

    private void CbScannerPageSize_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (cbScannerPageSize != null)
        {
            SettingsManager.Instance.Settings.ScannerPageSize = GetSelectedPageSizeSetting();
        }
    }

    private string GetSelectedPageSizeSetting()
    {
        return cbScannerPageSize?.SelectedIndex switch
        {
            1 => "A4",
            2 => "Letter",
            3 => "Legal",
            4 => "B5",
            5 => "A5",
            _ => "Auto"
        };
    }

    private async void ChkNetworkScanners_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        if (chkNetworkScanners != null)
        {
            SettingsManager.Instance.Settings.IncludeNetworkScanners = chkNetworkScanners.IsChecked ?? false;
            SettingsManager.Instance.Save();
            await RefreshScannersAsync(_cts.Token);
        }
    }

    private async void BtnRefreshScanners_Click(object? sender, RoutedEventArgs e)
    {
        await RefreshScannersAsync(_cts.Token);
    }

    private void SaveSettings()
    {
        var settings = SettingsManager.Instance.Settings;
        if (cbScanner.SelectedIndex >= 0 && cbScanner.SelectedIndex < _availableScanners.Count)
        {
            settings.SelectedScannerId = _availableScanners[cbScanner.SelectedIndex]?.Id ?? string.Empty;
        }
        if (cbScannerDpi?.SelectedItem is ComboBoxItem item)
        {
            string? content = item.Content?.ToString();
            int dpi = 300;
            if (content != null)
            {
                if (content.StartsWith("150", StringComparison.Ordinal)) dpi = 150;
                else if (content.StartsWith("600", StringComparison.Ordinal)) dpi = 600;
                else if (int.TryParse(content, out int parsed)) dpi = parsed;
            }
            settings.ScannerDpi = dpi;
        }
        if (cbScannerPageSize != null)
        {
            settings.ScannerPageSize = GetSelectedPageSizeSetting();
        }
        if (chkNetworkScanners != null)
        {
            settings.IncludeNetworkScanners = chkNetworkScanners.IsChecked ?? false;
        }
        SettingsManager.Instance.Save();
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(ScannerConfigResult.Cancel);
    }

    private void BtnSave_Click(object? sender, RoutedEventArgs e)
    {
        SaveSettings();
        Close(ScannerConfigResult.Save);
    }

    private void BtnSaveAndScan_Click(object? sender, RoutedEventArgs e)
    {
        SaveSettings();
        Close(ScannerConfigResult.SaveAndScan);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
        _refreshLock.Dispose();
        if (_ownsService)
        {
            _scannerService.Dispose();
        }
    }
}
