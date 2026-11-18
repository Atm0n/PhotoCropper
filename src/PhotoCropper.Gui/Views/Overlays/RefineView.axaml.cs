using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using PhotoCropper.Gui.Services;
using PhotoCropper.Gui.ViewModels;

namespace PhotoCropper.Gui.Views.Overlays;

/// <summary>Refine mode: drag a rectangle on the photo to set its new crop. Mapping the pointer onto the photo is all this does.</summary>
internal sealed partial class RefineView : UserControl
{
    private Point _start;
    private bool _dragging;

    public RefineView()
    {
        InitializeComponent();
    }

    private RefineViewModel? ViewModel => DataContext as RefineViewModel;

    /// <summary>Uses the detection box colour for the rectangle drawn while selecting.</summary>
    public void SetSelectionColor(Avalonia.Media.Color stroke, Avalonia.Media.Color fill)
    {
        rectRefineCrop.Stroke = new Avalonia.Media.SolidColorBrush(stroke);
        rectRefineCrop.Fill = new Avalonia.Media.SolidColorBrush(fill);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is not { IsOpen: true }) return;
        _start = e.GetPosition(pnlRefineImage);
        _dragging = true;
        rectRefineCrop.IsVisible = true;
        Canvas.SetLeft(rectRefineCrop, _start.X);
        Canvas.SetTop(rectRefineCrop, _start.Y);
        rectRefineCrop.Width = 0;
        rectRefineCrop.Height = 0;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;
        var rect = CoordinateMapper.ComputeNormalizedRect(_start, e.GetPosition(pnlRefineImage));
        Canvas.SetLeft(rectRefineCrop, rect.X);
        Canvas.SetTop(rectRefineCrop, rect.Y);
        rectRefineCrop.Width = rect.Width;
        rectRefineCrop.Height = rect.Height;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        rectRefineCrop.IsVisible = false;

        var rect = CoordinateMapper.ComputeNormalizedRect(_start, e.GetPosition(pnlRefineImage));
        if (rect.Width < 5 || rect.Height < 5 || ViewModel is not { IsOpen: true } vm) return;

        vm.SetCrop(CoordinateMapper.MapUiRectToImageRect(rect, ImageRectInPanel(), vm.PhotoSize));
    }

    /// <summary>Where the photo is drawn inside the panel (uniform fit inside its 20 px margin).</summary>
    private Rect ImageRectInPanel()
    {
        if (imgRefine?.Source == null) return default;

        var panel = pnlRefineImage.Bounds.Size;
        var image = imgRefine.Source.Size;
        double availableWidth = panel.Width - 40;
        double availableHeight = panel.Height - 40;
        double scale = Math.Min(availableWidth / image.Width, availableHeight / image.Height);

        double w = image.Width * scale;
        double h = image.Height * scale;
        return new Rect(20 + (availableWidth - w) / 2, 20 + (availableHeight - h) / 2, w, h);
    }
}
