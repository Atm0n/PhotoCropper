using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PhotoCropper.Gui.Services;

namespace PhotoCropper.Gui;

internal sealed partial class MainWindow
{
    private void ApplySettingsToUi()
    {
        var settings = SettingsManager.Instance.Settings;
        sldSensitivity.Value = PhotoCropper.Core.Models.DetectionOptions.ToleranceToSensitivity(settings.BackgroundTolerance);
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
                string headerText = LocalizationService.GetString(key, value);
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
                string headerText = LocalizationService.GetString(key, value);
                bool isSelected = string.Equals(currentColor, value, StringComparison.OrdinalIgnoreCase);
                var item = new MenuItem
                {
                    Header = CreateColorMenuItemHeader(isSelected, Color.Parse(hex), headerText),
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

            menu.Items.Add(new Separator());

            bool isCustomSelected = !string.IsNullOrWhiteSpace(currentColor) && currentColor.StartsWith('#');
            Color customColor = isCustomSelected && Color.TryParse(currentColor, out var parsedCustom)
                ? parsedCustom
                : Color.Parse("#00D4FF");

            string customBaseText = LocalizationService.GetString(ResourceKeys.ColorCustom, "Custom Color...");
            string customText = isCustomSelected ? $"{customBaseText} ({currentColor})" : customBaseText;

            var customItem = new MenuItem
            {
                Header = CreateColorMenuItemHeader(isCustomSelected, customColor, customText),
                Focusable = false,
                IsTabStop = false
            };

            customItem.Click += async (_, _) =>
            {
                var dialog = new Dialogs.CustomColorDialog(SettingsManager.Instance.Settings.DetectionBoxColor);
                string? chosenHex = await dialog.ShowDialog<string?>(this);
                if (!string.IsNullOrEmpty(chosenHex))
                {
                    await SetDetectionBoxColorAsync(chosenHex);
                }
            };

            menu.Items.Add(customItem);
        }
    }

    private static StackPanel CreateColorMenuItemHeader(bool isSelected, Color swatchColor, string text)
    {
        var panel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        var check = new TextBlock
        {
            Text = isSelected ? "✓" : " ",
            Width = 14,
            FontWeight = isSelected ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        var swatch = new Border
        {
            Width = 13,
            Height = 13,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(swatchColor),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 128, 128, 128)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        var label = new TextBlock
        {
            Text = text,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        panel.Children.Add(check);
        panel.Children.Add(swatch);
        panel.Children.Add(label);

        return panel;
    }

    private async Task SetDetectionBoxColorAsync(string colorName)
    {
        SettingsManager.Instance.Settings.DetectionBoxColor = colorName;
        SettingsManager.Instance.Save();

        UpdateCropStrokeColor(colorName);
        PopulateDetectionColorMenu();

        if (ScanSessions.Count > 0)
        {
            var session = ScanSessions[CurrentIndex];
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

        Color stroke;
        if (!string.IsNullOrWhiteSpace(colorName) && colorName.StartsWith('#') && Color.TryParse(colorName, out var parsed))
        {
            stroke = parsed;
        }
        else
        {
            stroke = colorName?.ToUpperInvariant() switch
            {
                "AMBER" or "ORANGE" => Color.Parse("#ffaa00"),
                "CYAN" or "BLUE" => Color.Parse("#00d4ff"),
                "MAGENTA" => Color.Parse("#ff00cc"),
                "LIME" => Color.Parse("#00e676"),
                _ => Color.Parse("#ff3333")
            };
        }

        var fill = Color.FromArgb(0x33, stroke.R, stroke.G, stroke.B);
        rectCrop.Stroke = new SolidColorBrush(stroke);
        rectCrop.Fill = new SolidColorBrush(fill);

        if (rectRefineCrop != null)
        {
            rectRefineCrop.Stroke = new SolidColorBrush(stroke);
            rectRefineCrop.Fill = new SolidColorBrush(fill);
        }
    }
}
