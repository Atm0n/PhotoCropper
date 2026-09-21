using Avalonia.Interactivity;
using Avalonia.Threading;
using PhotoCropper.Core.Updates;
using PhotoCropper.Gui.Services;

namespace PhotoCropper.Gui;

internal sealed partial class MainWindow
{
    private void CheckForUpdatesOnStartupIfDue()
    {
        var settings = SettingsManager.Instance.Settings;
        if (!settings.CheckForUpdatesAutomatically) return;

        DateTime? lastCheck = settings.LastUpdateCheckUtc;
        if (lastCheck == null || (DateTime.UtcNow - lastCheck.Value) >= TimeSpan.FromDays(1))
        {
            settings.LastUpdateCheckUtc = DateTime.UtcNow;
            SettingsManager.Instance.Save();

            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await UpdateCheckService.CheckForUpdateAsync(cancellationToken: CancellationToken.None);
                    if (result.IsUpdateAvailable && result.Tag != null && result.ReleaseUri != null)
                    {
                        var releaseUri = result.ReleaseUri;
                        Dispatcher.UIThread.Post(() =>
                        {
                            string template = LocalizationManager.GetString("TxtUpdateAvailable") ?? "PhotoCropper {0} is available!";
                            string msg = string.Format(System.Globalization.CultureInfo.CurrentCulture, template, result.Tag);
                            _notificationService.ShowInformation(
                                "PhotoCropper",
                                msg,
                                TimeSpan.FromSeconds(8),
                                onClick: () => _ = UrlLauncher.OpenUrlAsync(this, releaseUri));
                        });
                    }
                }
                catch
                {
                    // Ignore background update check failures
                }
            }, CancellationToken.None);
        }
    }

    private async void MenuCheckUpdates_Click(object? sender, RoutedEventArgs e)
    {
        string checkingMsg = LocalizationManager.GetString("TxtCheckingUpdates") ?? "Checking for updates...";
        _notificationService.ShowInformation("PhotoCropper", checkingMsg, TimeSpan.FromSeconds(2));

        var curVersion = UpdateCheckService.GetCurrentVersion();
        var result = await UpdateCheckService.CheckForUpdateAsync(curVersion, CancellationToken.None);

        SettingsManager.Instance.Settings.LastUpdateCheckUtc = DateTime.UtcNow;
        SettingsManager.Instance.Save();

        if (result.IsUpdateAvailable && result.Tag != null && result.ReleaseUri != null)
        {
            var releaseUri = result.ReleaseUri;
            string template = LocalizationManager.GetString("TxtUpdateAvailable") ?? "PhotoCropper {0} is available!";
            string msg = string.Format(System.Globalization.CultureInfo.CurrentCulture, template, result.Tag);
            _notificationService.ShowInformation(
                "PhotoCropper",
                msg,
                TimeSpan.FromSeconds(10),
                onClick: () => _ = UrlLauncher.OpenUrlAsync(this, releaseUri));
        }
        else if (result.ErrorMessage != null)
        {
            string template = LocalizationManager.GetString("TxtUpdateError") ?? "Unable to check for updates: {0}";
            string msg = string.Format(System.Globalization.CultureInfo.CurrentCulture, template, result.ErrorMessage);
            _notificationService.ShowWarning("PhotoCropper", msg, TimeSpan.FromSeconds(5));
        }
        else
        {
            string template = LocalizationManager.GetString("TxtUpToDate") ?? "You are using the latest version ({0}).";
            string verStr = $"v{curVersion.Major}.{curVersion.Minor}.{Math.Max(0, curVersion.Build)}";
            string msg = string.Format(System.Globalization.CultureInfo.CurrentCulture, template, verStr);
            _notificationService.ShowSuccess("PhotoCropper", msg, TimeSpan.FromSeconds(4));
        }
    }
}
