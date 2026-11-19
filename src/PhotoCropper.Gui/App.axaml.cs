using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using OpenCvSharp;
using PhotoCropper.Gui.Services;

namespace PhotoCropper.Gui;

internal sealed partial class App : Application
{
    public override void Initialize()
    {
        Cv2.SetLogLevel(LogLevel.ERROR);
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Load persistent user settings
        SettingsManager.Instance.Load();

        // Initialize localization with preferred language
        LocalizationService.Initialize(SettingsManager.Instance.Settings.Language);

        // Apply saved theme preference
        ApplyTheme(SettingsManager.Instance.Settings.Theme);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        Dispatcher.UIThread.UnhandledException += OnUiThreadUnhandledException;

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Exceptions escaping UI event handlers would otherwise terminate the app and lose unsaved work. Log
    /// them, tell the user where the report is, and keep running so they can save.
    /// </summary>
    private void OnUiThreadUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        string? logPath = CrashLogger.WriteCrashLog(e.Exception, "UI thread");
        e.Handled = true;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow window })
        {
            window.ShowAppError(
                LocalizationService.GetString(ResourceKeys.TitleUnexpectedError, "Unexpected Error"),
                LocalizationService.Format(
                    ResourceKeys.MsgUnexpectedError,
                    "Something went wrong: {0}\n\nYour work is still open; consider saving it now.\nA crash report was saved to:\n{1}",
                    e.Exception.Message,
                    logPath ?? "-"));
        }
    }

    public static void ApplyTheme(string? theme)
    {
        if (Current == null) return;
        Current.RequestedThemeVariant = theme?.ToUpperInvariant() switch
        {
            "LIGHT" => Avalonia.Styling.ThemeVariant.Light,
            "SYSTEM" or "DEFAULT" => Avalonia.Styling.ThemeVariant.Default,
            _ => Avalonia.Styling.ThemeVariant.Dark
        };
    }
}
