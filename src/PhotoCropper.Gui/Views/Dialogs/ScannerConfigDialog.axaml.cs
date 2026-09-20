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

        await RefreshScannersAsync();
    }

    private async Task RefreshScannersAsync()
    {
        if (txtScannerStatus != null)
        {
            txtScannerStatus.Text = Avalonia.Application.Current?.FindResource("TxtScanningSearching")?.ToString() ?? "Searching for connected scanners...";
            txtScannerStatus.Foreground = (Avalonia.Application.Current?.FindResource("AppAccentBrush") as IBrush) ?? Brush.Parse("#3399ff");
        }

        try
        {
            var settings = SettingsManager.Instance.Settings;
            bool includeNetwork = settings.IncludeNetworkScanners;
            var devices = await _scannerService.GetDevicesAsync(includeNetwork).ConfigureAwait(false);
            _availableScanners.Clear();
            _availableScanners.AddRange(devices);

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cbScanner != null)
                {
                    cbScanner.ItemsSource = _availableScanners.Select(d => d.ToString()).ToList();

                    int selectedIdx = _availableScanners.FindIndex(d => d.Id == settings.SelectedScannerId);
                    if (selectedIdx >= 0)
                    {
                        cbScanner.SelectedIndex = selectedIdx;
                    }
                    else if (_availableScanners.Count > 0)
                    {
                        cbScanner.SelectedIndex = 0;
                    }
                }

                UpdateStatusLabel();
            });
        }
        catch
        {
            UpdateStatusLabel();
        }
    }

    private void UpdateStatusLabel()
    {
        if (txtScannerStatus == null) return;

        if (_availableScanners.Count == 0)
        {
            txtScannerStatus.Text = Avalonia.Application.Current?.FindResource("MsgNoScannerFound")?.ToString() ?? "No scanner detected. Click 🔄 to refresh.";
            txtScannerStatus.Foreground = (Avalonia.Application.Current?.FindResource("AppDangerTextBrush") as IBrush) ?? Brush.Parse("#ffaa44");
        }
        else
        {
            txtScannerStatus.Text = $"{_availableScanners.Count} scanner(s) found.";
            txtScannerStatus.Foreground = (Avalonia.Application.Current?.FindResource("AppSuccessTextBrush") as IBrush) ?? Brush.Parse("#44cc66");
        }
    }

    private void CbScanner_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (cbScanner.SelectedIndex >= 0 && cbScanner.SelectedIndex < _availableScanners.Count)
        {
            SettingsManager.Instance.Settings.SelectedScannerId = _availableScanners[cbScanner.SelectedIndex].Id;
        }
    }

    private void CbScannerDpi_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
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

    private async void ChkNetworkScanners_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (chkNetworkScanners != null)
        {
            SettingsManager.Instance.Settings.IncludeNetworkScanners = chkNetworkScanners.IsChecked ?? false;
            SettingsManager.Instance.Save();
            await RefreshScannersAsync();
        }
    }

    private async void BtnRefreshScanners_Click(object? sender, RoutedEventArgs e)
    {
        await RefreshScannersAsync();
    }

    private void SaveSettings()
    {
        var settings = SettingsManager.Instance.Settings;
        if (cbScanner.SelectedIndex >= 0 && cbScanner.SelectedIndex < _availableScanners.Count)
        {
            settings.SelectedScannerId = _availableScanners[cbScanner.SelectedIndex].Id;
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
        if (_ownsService)
        {
            _scannerService.Dispose();
        }
    }
}
