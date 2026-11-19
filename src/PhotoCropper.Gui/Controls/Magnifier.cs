using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using PhotoCropper.Core.Models;

namespace PhotoCropper.Gui.Controls;

/// <summary>
/// A zoomed view of the scan around a point, shown while a box corner is dragged or a box is drawn, so its edge can be
/// placed on the photo's edge without precise mouse work. The boxes are drawn as thin lines (the overlay's thick
/// outline would hide the very edge being aligned), with a crosshair on the point.
/// </summary>
internal sealed class Magnifier : Control
{
    private static readonly IBrush Backdrop = new ImmutableSolidColorBrush(Color.FromRgb(32, 32, 32));
    private static readonly IPen FramePen = new ImmutablePen(new ImmutableSolidColorBrush(Colors.White), 2);
    private static readonly IPen CrossOuterPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(160, 0, 0, 0)), 3);
    private static readonly IPen CrossInnerPen = new ImmutablePen(new ImmutableSolidColorBrush(Colors.White), 1);

    private Bitmap? _source;
    private Point _center;
    private double _zoom = 1;
    private IReadOnlyList<CropCandidate>? _boxes;
    private Color _boxColor = Colors.Red;

    public Magnifier()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        // Show the scan's pixels as they are: smoothing them would blur the edge being aligned.
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
    }

    /// <summary>Shows the scan around <paramref name="center"/> (scan pixels), <paramref name="zoom"/> screen pixels per scan pixel.</summary>
    /// <param name="source">The scan bitmap, one pixel per scan pixel.</param>
    public void Show(Bitmap source, Point center, double zoom, IReadOnlyList<CropCandidate>? boxes, Color boxColor)
    {
        _source = source;
        _center = center;
        _zoom = Math.Max(0.01, zoom);
        _boxes = boxes;
        _boxColor = boxColor;
        IsVisible = true;
        InvalidateVisual();
    }

    public void Hide()
    {
        IsVisible = false;
        _source = null;
        _boxes = null;
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) return;
        var frame = new Rect(size);
        context.FillRectangle(Backdrop, frame);

        if (_source is { } source)
        {
            // The part of the scan shown, in scan pixels, centred on the point.
            double w = size.Width / _zoom, h = size.Height / _zoom;
            var shown = new Rect(_center.X - w / 2, _center.Y - h / 2, w, h);
            Point ToView(double x, double y) => new((x - shown.X) * _zoom, (y - shown.Y) * _zoom);

            var inScan = shown.Intersect(new Rect(0, 0, source.PixelSize.Width, source.PixelSize.Height));
            if (inScan.Width > 0 && inScan.Height > 0)
            {
                var topLeft = ToView(inScan.X, inScan.Y);
                context.DrawImage(source, inScan, new Rect(topLeft, new Size(inScan.Width * _zoom, inScan.Height * _zoom)));
            }

            if (_boxes is { Count: > 0 } boxes)
            {
                var pen = new ImmutablePen(new ImmutableSolidColorBrush(_boxColor), 1.5);
                foreach (var box in boxes)
                {
                    var corners = box.Rotated.GetVertices();
                    var outline = new StreamGeometry();
                    using (var ctx = outline.Open())
                    {
                        ctx.BeginFigure(ToView(corners[0].X, corners[0].Y), false);
                        for (int i = 1; i < corners.Length; i++) ctx.LineTo(ToView(corners[i].X, corners[i].Y));
                        ctx.EndFigure(true);
                    }
                    context.DrawGeometry(null, pen, outline);
                }
            }
        }

        // Crosshair with a gap in the middle, so the pixel under the point stays visible.
        // On a pixel's centre, so the 1-unit lines are sharp.
        double cx = Math.Floor(size.Width / 2) + 0.5, cy = Math.Floor(size.Height / 2) + 0.5, gap = 6, arm = 18;
        foreach (var pen in new[] { CrossOuterPen, CrossInnerPen })
        {
            context.DrawLine(pen, new Point(cx - gap - arm, cy), new Point(cx - gap, cy));
            context.DrawLine(pen, new Point(cx + gap, cy), new Point(cx + gap + arm, cy));
            context.DrawLine(pen, new Point(cx, cy - gap - arm), new Point(cx, cy - gap));
            context.DrawLine(pen, new Point(cx, cy + gap), new Point(cx, cy + gap + arm));
        }
        context.DrawRectangle(null, FramePen, frame.Deflate(1));
    }
}
