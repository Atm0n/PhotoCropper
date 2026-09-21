using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PhotoCropper.Core.Updates;
using PhotoCropper.Gui.Services;

namespace PhotoCropper.Gui.Dialogs;

internal sealed partial class HelpWindow : Window
{
    private Uri? _latestReleaseUri;

    public HelpWindow()
    {
        InitializeComponent();
        KeyDown += HelpWindow_KeyDown;

        var ver = UpdateCheckService.GetCurrentVersion();
        txtAppVersion.Text = $"v{ver.Major}.{ver.Minor}.{Math.Max(0, ver.Build)}";
    }

    private void HelpWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private async void BtnCheckUpdates_Click(object? sender, RoutedEventArgs e)
    {
        btnCheckUpdates.IsEnabled = false;
        txtUpdateStatus.Text = LocalizationManager.GetString("TxtCheckingUpdates") ?? "Checking for updates...";
        btnOpenRelease.IsVisible = false;

        try
        {
            var curVersion = UpdateCheckService.GetCurrentVersion();
            var result = await UpdateCheckService.CheckForUpdateAsync(curVersion);

            if (result.IsUpdateAvailable && result.Tag != null && result.ReleaseUri != null)
            {
                _latestReleaseUri = result.ReleaseUri;
                string template = LocalizationManager.GetString("TxtUpdateAvailable") ?? "PhotoCropper {0} is available!";
                txtUpdateStatus.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, template, result.Tag);
                btnOpenRelease.IsVisible = true;
            }
            else if (result.ErrorMessage != null)
            {
                string template = LocalizationManager.GetString("TxtUpdateError") ?? "Unable to check for updates: {0}";
                txtUpdateStatus.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, template, result.ErrorMessage);
            }
            else
            {
                string template = LocalizationManager.GetString("TxtUpToDate") ?? "You are using the latest version ({0}).";
                string verStr = $"v{curVersion.Major}.{curVersion.Minor}.{Math.Max(0, curVersion.Build)}";
                txtUpdateStatus.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, template, verStr);
            }
        }
        catch (Exception ex)
        {
            txtUpdateStatus.Text = ex.Message;
        }
        finally
        {
            btnCheckUpdates.IsEnabled = true;
        }
    }

    private async void BtnOpenRelease_Click(object? sender, RoutedEventArgs e)
    {
        if (_latestReleaseUri != null)
        {
            await UrlLauncher.OpenUrlAsync(this, _latestReleaseUri);
        }
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
