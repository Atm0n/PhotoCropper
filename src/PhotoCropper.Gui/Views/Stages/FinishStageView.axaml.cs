using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using PhotoCropper.Gui.ViewModels;

namespace PhotoCropper.Gui.Views.Stages;

/// <summary>
/// Finish stage. The only code here is the repair brush's pointer handling: it draws the stroke live and hands the
/// finished stroke (as fractions of the photo's size) to the view-model, which stores and renders it.
/// </summary>
internal sealed partial class FinishStageView : UserControl
{
    private static readonly IBrush StrokeBrush = new SolidColorBrush(Color.FromArgb(150, 255, 60, 60));

    private Polyline? _liveStroke;
    private readonly List<(double X, double Y)> _strokePoints = [];

    public FinishStageView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is FinishStageViewModel vm)
            {
                // The painted line stays until the repaired preview replaces it.
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(FinishStageViewModel.AfterPreview) or nameof(FinishStageViewModel.SelectedPhoto)
                        or nameof(FinishStageViewModel.IsRepairMode))
                    {
                        ClearStrokeOverlay();
                    }
                };
            }
        };
    }

    private FinishStageViewModel? ViewModel => DataContext as FinishStageViewModel;

    /// <summary>Where the photo is drawn inside the surface (Stretch="Uniform" letterboxes it).</summary>
    private Rect? DisplayedImageRect()
    {
        if (imgRepair.Source is not Avalonia.Media.Imaging.Bitmap bitmap) return null;
        Size area = pnlRepairSurface.Bounds.Size;
        if (area.Width <= 0 || area.Height <= 0) return null;

        double scale = Math.Min(area.Width / bitmap.PixelSize.Width, area.Height / bitmap.PixelSize.Height);
        double width = bitmap.PixelSize.Width * scale;
        double height = bitmap.PixelSize.Height * scale;
        return new Rect((area.Width - width) / 2, (area.Height - height) / 2, width, height);
    }

    /// <summary>Brush diameter on screen, matching the radius the view-model will store.</summary>
    private double BrushDiameterOnScreen(Rect image) => (ViewModel?.BrushRadius ?? 0.008) * image.Width * 2;

    private void RepairSurface_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel == null || DisplayedImageRect() is not Rect image) return;
        if (!e.GetCurrentPoint(pnlRepairSurface).Properties.IsLeftButtonPressed) return;

        Point position = e.GetPosition(pnlRepairSurface);
        if (!image.Contains(position)) return;

        ClearStrokeOverlay();
        _liveStroke = new Polyline
        {
            Stroke = StrokeBrush,
            StrokeThickness = Math.Max(1, BrushDiameterOnScreen(image)),
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            IsHitTestVisible = false
        };
        cnvRepairStroke.Children.Add(_liveStroke);
        AddPoint(position, image);

        e.Pointer.Capture(pnlRepairSurface);
        e.Handled = true;
    }

    private void RepairSurface_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (DisplayedImageRect() is not Rect image) return;
        Point position = e.GetPosition(pnlRepairSurface);
        MoveBrushCursor(position, image);

        if (_liveStroke == null) return;

        // Skip tiny moves: they add points without changing the stroke.
        if (_liveStroke.Points.Count > 0)
        {
            Point last = _liveStroke.Points[^1];
            if (Math.Abs(last.X - position.X) + Math.Abs(last.Y - position.Y) < 2) return;
        }
        AddPoint(position, image);
    }

    private void RepairSurface_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_liveStroke == null) return;
        e.Pointer.Capture(null);

        // A click without moving still paints a dot (the first point already covers it).
        ViewModel?.AddRepairStroke([.. _strokePoints]);
        _strokePoints.Clear();
        _liveStroke = null; // the drawn line stays visible until the repaired preview arrives
    }

    private void RepairSurface_PointerExited(object? sender, PointerEventArgs e) => ellBrushCursor.IsVisible = false;

    private void AddPoint(Point position, Rect image)
    {
        Point clamped = new(Math.Clamp(position.X, image.Left, image.Right), Math.Clamp(position.Y, image.Top, image.Bottom));
        _liveStroke!.Points.Add(clamped);
        if (_liveStroke.Points.Count == 1) _liveStroke.Points.Add(clamped); // a single point needs two to render as a dot

        _strokePoints.Add(((clamped.X - image.Left) / image.Width, (clamped.Y - image.Top) / image.Height));
    }

    private void MoveBrushCursor(Point position, Rect image)
    {
        double diameter = Math.Max(4, BrushDiameterOnScreen(image));
        ellBrushCursor.Width = diameter;
        ellBrushCursor.Height = diameter;
        Canvas.SetLeft(ellBrushCursor, position.X - diameter / 2);
        Canvas.SetTop(ellBrushCursor, position.Y - diameter / 2);
        ellBrushCursor.IsVisible = image.Contains(position);
    }

    private void ClearStrokeOverlay()
    {
        // Keep the brush cursor and the stroke being painted right now (a preview of an earlier stroke may arrive
        // mid-stroke); remove finished lines, which the repaired preview now shows.
        for (int i = cnvRepairStroke.Children.Count - 1; i >= 0; i--)
        {
            if (cnvRepairStroke.Children[i] is Polyline line && !ReferenceEquals(line, _liveStroke))
            {
                cnvRepairStroke.Children.RemoveAt(i);
            }
        }
        if (_liveStroke == null) _strokePoints.Clear();
    }
}
