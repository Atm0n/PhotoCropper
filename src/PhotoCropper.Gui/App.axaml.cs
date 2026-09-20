using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace PhotoCropper.Gui;

internal sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Load persistent user settings
        SettingsManager.Instance.Load();

        // Initialize localization with preferred language
        LocalizationManager.Initialize(SettingsManager.Instance.Settings.Language);

        // Apply saved theme preference
        ApplyTheme(SettingsManager.Instance.Settings.Theme);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
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