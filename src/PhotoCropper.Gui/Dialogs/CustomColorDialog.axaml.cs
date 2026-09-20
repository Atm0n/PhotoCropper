using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace PhotoCropper.Gui.Dialogs;

internal sealed partial class CustomColorDialog : Window
{
    private static readonly string[] PresetSwatches =
    [
        "#00FF66", "#00E5FF", "#0099FF", "#7C4DFF",
        "#D500F9", "#FF007F", "#FF3333", "#FF6D00",
        "#FFAB00", "#FFD600", "#AEEA00", "#00E676",
        "#1DE9B6", "#E040FB", "#40C4FF", "#FFFFFF"
    ];

    private bool _isUpdating;
    private Color _currentColor = Color.Parse("#00D4FF");

    public CustomColorDialog() : this("#00D4FF")
    {
    }

    public CustomColorDialog(string? initialColor)
    {
        InitializeComponent();
        KeyDown += CustomColorDialog_KeyDown;

        Color startColor = ParseInitialColor(initialColor);
        BuildSwatchButtons();
        SetColor(startColor);
    }

    private static Color ParseInitialColor(string? initialColor)
    {
        if (!string.IsNullOrWhiteSpace(initialColor))
        {
            if (initialColor.StartsWith('#') && Color.TryParse(initialColor, out var parsed))
            {
                return parsed;
            }

            return initialColor.ToUpperInvariant() switch
            {
                "AMBER" or "ORANGE" => Color.Parse("#FFAA00"),
                "CYAN" or "BLUE" => Color.Parse("#00D4FF"),
                "MAGENTA" => Color.Parse("#FF00CC"),
                "LIME" => Color.Parse("#00E676"),
                _ => Color.Parse("#FF3333")
            };
        }

        return Color.Parse("#00D4FF");
    }

    private void BuildSwatchButtons()
    {
        if (pnlSwatches == null) return;

        foreach (string hex in PresetSwatches)
        {
            var swatchColor = Color.Parse(hex);
            var button = new Button
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(swatchColor),
                BorderBrush = new SolidColorBrush(Color.FromArgb(100, 128, 128, 128)),
                BorderThickness = new Thickness(1),
                Focusable = false,
                Tag = hex
            };

            button.Click += (_, _) =>
            {
                SetColor(swatchColor);
            };

            pnlSwatches.Children.Add(button);
        }
    }

    private void SetColor(Color color)
    {
        _currentColor = color;
        _isUpdating = true;
        try
        {
            sliderR.Value = color.R;
            sliderG.Value = color.G;
            sliderB.Value = color.B;

            txtValR.Text = color.R.ToString();
            txtValG.Text = color.G.ToString();
            txtValB.Text = color.B.ToString();

            string hexStr = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            txtHex.Text = hexStr;

            UpdatePreviewUi(color);
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private void UpdatePreviewUi(Color color)
    {
        var brush = new SolidColorBrush(color);
        var fillBrush = new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B));

        borderPreview.Background = brush;
        borderBoxSim.BorderBrush = brush;
        borderBoxSim.Background = fillBrush;
    }

    private void Slider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdating) return;

        byte r = (byte)Math.Clamp((int)sliderR.Value, 0, 255);
        byte g = (byte)Math.Clamp((int)sliderG.Value, 0, 255);
        byte b = (byte)Math.Clamp((int)sliderB.Value, 0, 255);

        _currentColor = Color.FromRgb(r, g, b);

        txtValR.Text = r.ToString();
        txtValG.Text = g.ToString();
        txtValB.Text = b.ToString();

        _isUpdating = true;
        try
        {
            txtHex.Text = $"#{r:X2}{g:X2}{b:X2}";
            UpdatePreviewUi(_currentColor);
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private void TxtHex_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdating) return;

        string text = txtHex.Text?.Trim() ?? "";
        if (!text.StartsWith('#')) text = "#" + text;

        if (Color.TryParse(text, out var color))
        {
            _currentColor = color;
            _isUpdating = true;
            try
            {
                sliderR.Value = color.R;
                sliderG.Value = color.G;
                sliderB.Value = color.B;

                txtValR.Text = color.R.ToString();
                txtValG.Text = color.G.ToString();
                txtValB.Text = color.B.ToString();

                UpdatePreviewUi(color);
            }
            finally
            {
                _isUpdating = false;
            }
        }
    }

    private void CustomColorDialog_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ApplyAndClose();
            e.Handled = true;
        }
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void BtnApply_Click(object? sender, RoutedEventArgs e)
    {
        ApplyAndClose();
    }

    private void ApplyAndClose()
    {
        string hex = $"#{_currentColor.R:X2}{_currentColor.G:X2}{_currentColor.B:X2}";
        Close(hex);
    }
}
