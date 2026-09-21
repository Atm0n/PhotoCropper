using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;

namespace PhotoCropper.Gui.Services;

internal sealed class AppNotificationService
{
    private WindowNotificationManager? _notificationManager;
    private Window? _window;

    public void Initialize(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        _notificationManager = new WindowNotificationManager(window)
        {
            Position = NotificationPosition.BottomRight,
            MaxItems = 4
        };
    }

    public void ShowSuccess(string title, string message, TimeSpan? expiration = null, Action? onClick = null)
    {
        ShowNotification(title, message, NotificationType.Success, expiration ?? TimeSpan.FromSeconds(4), onClick);
    }

    public void ShowInformation(string title, string message, TimeSpan? expiration = null, Action? onClick = null)
    {
        ShowNotification(title, message, NotificationType.Information, expiration ?? TimeSpan.FromSeconds(4), onClick);
    }

    public void ShowWarning(string title, string message, TimeSpan? expiration = null, Action? onClick = null)
    {
        ShowNotification(title, message, NotificationType.Warning, expiration ?? TimeSpan.FromSeconds(5), onClick);
    }

    public void ShowError(string title, string message, TimeSpan? expiration = null, Action? onClick = null)
    {
        ShowNotification(title, message, NotificationType.Error, expiration ?? TimeSpan.FromSeconds(6), onClick);
    }

    public void NotifyExportCompleted(int photoCount, string destinationFolder)
    {
        ArgumentNullException.ThrowIfNull(destinationFolder);

        var settings = SettingsManager.Instance.Settings;

        if (settings.FlashTaskbarOnCompletion && _window != null)
        {
            WindowsTaskbarHelper.FlashWindow(_window);
        }

        if (settings.ShowNotifications)
        {
            string title = "PhotoCropper";
            string folderName = System.IO.Path.GetFileName(destinationFolder);
            string message = photoCount == 1
                ? $"Successfully exported 1 photo to '{folderName}'."
                : $"Successfully exported {photoCount} photos to '{folderName}'.";

            ShowSuccess(title, message, TimeSpan.FromSeconds(4));
        }
    }

    private void ShowNotification(string title, string message, NotificationType type, TimeSpan expiration, Action? onClick = null)
    {
        var settings = SettingsManager.Instance.Settings;
        if (!settings.ShowNotifications && type != NotificationType.Error)
        {
            return;
        }

        void Display()
        {
            _notificationManager?.Show(new Notification(title, message, type, expiration, onClick));
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Display();
        }
        else
        {
            Dispatcher.UIThread.Post(Display);
        }
    }
}
