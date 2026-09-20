using Avalonia.Controls;
using Avalonia.Media;
using PhotoCropper.Core.Models;

namespace PhotoCropper.Gui;

internal sealed partial class MainWindow
{
    private void ApplySettingsToUi()
    {
        var settings = SettingsManager.Instance.Settings;
        sldSensitivity.Value = settings.BackgroundTolerance;
        sldZoom.Value = settings.ZoomLevel;
        sldMinArea.Value = settings.MinAreaFactor;
        sldMaxArea.Value = settings.MaxAreaFactor;
        sldEdge.Value = settings.CannyLowThreshold;
        tglAdvanced.IsChecked = settings.AdvancedVisible;
        if (chkAutoOrient != null)
        {
            chkAutoOrient.IsChecked = settings.AutoOrientPhotos;
        }
        if (chkRestoreColors != null)
        {
            chkRestoreColors.IsChecked = settings.RestoreVintageColors;
        }
        if (chkRemoveDust != null)
        {
            chkRemoveDust.IsChecked = settings.RemoveDustAndScratches;
        }

        if (cbFormat != null)
        {
            cbFormat.SelectedIndex = string.Equals(settings.PreferredFormat, "PNG", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }
        if (sldJpegQuality != null)
        {
            sldJpegQuality.Value = settings.JpegQuality;
        }
        if (txtOutputDir != null)
        {
            txtOutputDir.Text = settings.CustomOutputDirectory ?? "";
        }
        if (pnlJpegQuality != null)
        {
            pnlJpegQuality.IsVisible = !string.Equals(settings.PreferredFormat, "PNG", StringComparison.OrdinalIgnoreCase);
        }

        UpdateWorkspaceUi(settings.WorkDirectory);

        UpdateCropStrokeColor(settings.DetectionBoxColor);
        PopulateThemeMenu();
        PopulateDetectionColorMenu();
    }

    private void PopulateLanguageMenu()
    {
        var menus = new[] { menuLanguage, menuAltLanguage };
        var languages = LocalizationManager.GetAvailableLanguages();

        foreach (var menu in menus)
        {
            if (menu == null) continue;
            menu.Items.Clear();

            foreach (var lang in languages)
            {
                var item = new MenuItem
                {
                    Header = lang.Name,
                    Tag = lang.Code,
                    Focusable = false,
                    IsTabStop = false
                };
                item.Click += (s, _) =>
                {
                    if (s is MenuItem mi && mi.Tag is string code)
                    {
                        LocalizationManager.SetLanguage(code);
                        UpdateWorkspaceUi(SettingsManager.Instance.Settings.WorkDirectory);
                        UpdateSelectionUi();
                        PopulateThemeMenu();
                        PopulateDetectionColorMenu();
                        FocusManager?.Focus(null);
                    }
                };
                menu.Items.Add(item);
            }
        }
    }

    private void PopulateThemeMenu()
    {
        var menus = new[] { menuTheme, menuAltTheme };
        var currentTheme = SettingsManager.Instance.Settings.Theme ?? "Dark";

        var themeItems = new (string Key, string Value)[]
        {
            ("ThemeDark", "Dark"),
            ("ThemeLight", "Light"),
            ("ThemeSystem", "System")
        };

        foreach (var menu in menus)
        {
            if (menu == null) continue;
            menu.Items.Clear();

            foreach (var (key, value) in themeItems)
            {
                string headerText = Avalonia.Application.Current?.FindResource(key)?.ToString() ?? value;
                var item = new MenuItem
                {
                    Header = string.Equals(currentTheme, value, StringComparison.OrdinalIgnoreCase) ? $"✓ {headerText}" : $"   {headerText}",
                    Tag = value,
                    Focusable = false,
                    IsTabStop = false
                };
                item.Click += (s, _) =>
                {
                    if (s is MenuItem mi && mi.Tag is string chosenTheme)
                    {
                        SetTheme(chosenTheme);
                    }
                };
                menu.Items.Add(item);
            }
        }
    }

    private void SetTheme(string theme)
    {
        SettingsManager.Instance.Settings.Theme = theme;
        SettingsManager.Instance.Save();
        App.ApplyTheme(theme);
        RequestedThemeVariant = theme?.ToUpperInvariant() switch
        {
            "LIGHT" => Avalonia.Styling.ThemeVariant.Light,
            "SYSTEM" or "DEFAULT" => Avalonia.Styling.ThemeVariant.Default,
            _ => Avalonia.Styling.ThemeVariant.Dark
        };
        PopulateThemeMenu();
    }

    private void PopulateDetectionColorMenu()
    {
        var menus = new[] { menuDetectionColor, menuAltDetectionColor };
        var currentColor = SettingsManager.Instance.Settings.DetectionBoxColor ?? "Red";

        var colorItems = new (string Key, string Value, string Hex)[]
        {
            ("ColorRed", "Red", "#ff3333"),
            ("ColorAmber", "Amber", "#ffaa00"),
            ("ColorCyan", "Cyan", "#00d4ff"),
            ("ColorMagenta", "Magenta", "#ff00cc"),
            ("ColorLime", "Lime", "#00e676")
        };

        foreach (var menu in menus)
        {
            if (menu == null) continue;
            menu.Items.Clear();

            foreach (var (key, value, hex) in colorItems)
            {
                string headerText = Avalonia.Application.Current?.FindResource(key)?.ToString() ?? value;
                bool isSelected = string.Equals(currentColor, value, StringComparison.OrdinalIgnoreCase);
                var item = new MenuItem
                {
                    Header = isSelected ? $"✓ {headerText}" : $"   {headerText}",
                    Tag = value,
                    Focusable = false,
                    IsTabStop = false
                };
                item.Click += async (s, _) =>
                {
                    if (s is MenuItem mi && mi.Tag is string chosenColor)
                    {
                        await SetDetectionBoxColorAsync(chosenColor);
                    }
                };
                menu.Items.Add(item);
            }
        }
    }

    private async Task SetDetectionBoxColorAsync(string colorName)
    {
        SettingsManager.Instance.Settings.DetectionBoxColor = colorName;
        SettingsManager.Instance.Save();

        UpdateCropStrokeColor(colorName);
        PopulateDetectionColorMenu();

        if (ScanSessions.Count > 0)
        {
            var session = ScanSessions[currentIndex];
            session.Options.BoundingBoxColor = colorName;
            if (session.IsActive && session.Engine != null)
            {
                session.Engine.BoundingBoxColor = colorName;
            }
            await ReprocessCurrentScanAsync();
        }
    }

    private void UpdateCropStrokeColor(string? colorName)
    {
        if (rectCrop == null) return;

        var (stroke, fill) = colorName?.ToUpperInvariant() switch
        {
            "AMBER" or "ORANGE" => (Color.Parse("#ffaa00"), Color.Parse("#33ffaa00")),
            "CYAN" or "BLUE" => (Color.Parse("#00d4ff"), Color.Parse("#3300d4ff")),
            "MAGENTA" => (Color.Parse("#ff00cc"), Color.Parse("#33ff00cc")),
            "LIME" => (Color.Parse("#00e676"), Color.Parse("#3300e676")),
            _ => (Color.Parse("#ff3333"), Color.Parse("#33ff3333"))
        };

        rectCrop.Stroke = new SolidColorBrush(stroke);
        rectCrop.Fill = new SolidColorBrush(fill);

        if (rectRefineCrop != null)
        {
            rectRefineCrop.Stroke = new SolidColorBrush(stroke);
            rectRefineCrop.Fill = new SolidColorBrush(fill);
        }
    }
}
