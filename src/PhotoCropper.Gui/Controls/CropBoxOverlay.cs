using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using PhotoCropper.Core;
using PhotoCropper.Core.Extraction;
using PhotoCropper.Core.Models;

namespace PhotoCropper.Gui.Controls;

/// <summary>Where one box's overlay parts sit, in scan pixels. Drawing and hit-testing share it so they always agree.</summary>
internal readonly record struct CropBoxGeometry(
    System.Drawing.PointF[] Vertices,
    System.Drawing.PointF DeleteBadge,
    System.Drawing.PointF TopMid,
    System.Drawing.PointF RotationHandle)
{
    public static CropBoxGeometry For(CropCandidate candidate, System.Drawing.Size scanSize, double overlayScale)
    {
        System.Drawing.PointF[] vertices = candidate.Rotated.GetVertices();

        // The delete badge sits on the top-right corner: the vertex with the largest X - Y.
        System.Drawing.PointF deleteBadge = vertices.MaxBy(v => v.X - v.Y);

        System.Drawing.PointF[] ordered = PhotoExtractionEngine.OrderBoxPoints(vertices);
        var topMid = new System.Drawing.PointF((ordered[0].X + ordered[1].X) / 2f, (ordered[0].Y + ordered[1].Y) / 2f);

        var bounds = new System.Drawing.Rectangle(System.Drawing.Point.Empty, scanSize);
        System.Drawing.PointF rotationHandle = candidate.GetRotationHandlePoint((float)(PhotoCropperEngine.RotationHandleBaseOffset * overlayScale), bounds);

        return new CropBoxGeometry(vertices, deleteBadge, topMid, rotationHandle);
    }
}

/// <summary>
/// Draws the detected boxes over the scan: outline, corner grab handles, the red delete badge and the blue rotation
/// handle. It is drawn by Avalonia on top of the untouched scan, so editing a box only repaints the overlay instead
/// of re-rendering and re-uploading the whole scan bitmap. Sizes are in scan pixels (scaled with
/// <see cref="OverlayScale"/>), so the overlay keeps its size relative to the scan at any resolution and zoom.
/// </summary>
internal sealed class CropBoxOverlay : Control
{
    public static readonly StyledProperty<IReadOnlyList<CropCandidate>?> CandidatesProperty =
        AvaloniaProperty.Register<CropBoxOverlay, IReadOnlyList<CropCandidate>?>(nameof(Candidates));

    public static readonly StyledProperty<Size> ScanSizeProperty =
        AvaloniaProperty.Register<CropBoxOverlay, Size>(nameof(ScanSize));

    public static readonly StyledProperty<double> OverlayScaleProperty =
        AvaloniaProperty.Register<CropBoxOverlay, double>(nameof(OverlayScale), 1.0);

    public static readonly StyledProperty<Color> BoxColorProperty =
        AvaloniaProperty.Register<CropBoxOverlay, Color>(nameof(BoxColor), Colors.Red);

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<CropBoxOverlay, int>(nameof(SelectedIndex), -1);

    private static readonly ImmutableSolidColorBrush White = new(Colors.White);
    private static readonly IBrush DeleteBadgeBrush = new ImmutableSolidColorBrush(Color.FromRgb(255, 0, 0));
    private static readonly IBrush RotationBadgeBrush = new ImmutableSolidColorBrush(Color.FromRgb(0, 0, 255));
    private static readonly ImmutableSolidColorBrush OutlineBrush = new(Color.FromArgb(200, 20, 20, 20));

    static CropBoxOverlay()
    {
        AffectsRender<CropBoxOverlay>(CandidatesProperty, ScanSizeProperty, OverlayScaleProperty, BoxColorProperty, SelectedIndexProperty);
        ClipToBoundsProperty.OverrideDefaultValue<CropBoxOverlay>(true);
        IsHitTestVisibleProperty.OverrideDefaultValue<CropBoxOverlay>(false);
    }

    /// <summary>The boxes to draw, in scan pixels. Set a new list (or call <see cref="Control.InvalidateVisual"/>) to repaint.</summary>
    public IReadOnlyList<CropCandidate>? Candidates
    {
        get => GetValue(CandidatesProperty);
        set => SetValue(CandidatesProperty, value);
    }

    /// <summary>Size of the scan in pixels; the overlay is stretched from this to the control's bounds.</summary>
    public Size ScanSize
    {
        get => GetValue(ScanSizeProperty);
        set => SetValue(ScanSizeProperty, value);
    }

    /// <summary>The scan's <see cref="PhotoCropperEngine.OverlayScale"/>.</summary>
    public double OverlayScale
    {
        get => GetValue(OverlayScaleProperty);
        set => SetValue(OverlayScaleProperty, value);
    }

    public Color BoxColor
    {
        get => GetValue(BoxColorProperty);
        set => SetValue(BoxColorProperty, value);
    }

    /// <summary>The selected box (index into <see cref="Candidates"/>), the only one drawn with its handles; -1 for none.</summary>
    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var candidates = Candidates;
        Size scan = ScanSize;
        if (candidates == null || candidates.Count == 0 || scan.Width <= 0 || scan.Height <= 0 || Bounds.Width <= 0) return;

        // Draw in scan pixels; the transform maps them onto the displayed image.
        using var transform = context.PushTransform(Matrix.CreateScale(Bounds.Width / scan.Width, Bounds.Height / scan.Height));

        double s = OverlayScale;
        double Scaled(double basePixels) => Math.Max(1, Math.Round(basePixels * s));

        var boxBrush = new ImmutableSolidColorBrush(BoxColor);
        // Each box is a thin line in the box colour over a slightly wider dark one (visible on light and dark scans
        // alike), drawn just outside the box so it never covers the photo's own edge, which is what is being matched.
        var outlinePen = new ImmutablePen(OutlineBrush, Scaled(10), lineJoin: PenLineJoin.Miter);
        var boxPen = new ImmutablePen(boxBrush, Scaled(6), lineJoin: PenLineJoin.Miter);
        double outside = Scaled(10) / 2 + Scaled(2);
        var handleBorderPen = new ImmutablePen(boxBrush, Scaled(4));
        var stemPen = new ImmutablePen(boxBrush, Scaled(6));
        var crossPen = new ImmutablePen(White, Scaled(8));
        var badgeRingPen = new ImmutablePen(White, Scaled(3));
        var arrowPen = new ImmutablePen(White, Scaled(5));

        double radius = Scaled(45);
        double handleHalf = Scaled(25);
        double crossHalf = Scaled(15);
        double arrowRadius = Scaled(22);
        var scanRect = new Rect(scan);
        var scanSize = new System.Drawing.Size((int)scan.Width, (int)scan.Height);

        for (int index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var geometry = CropBoxGeometry.For(candidate, scanSize, s);
            Point[] corners = [.. geometry.Vertices.Select(ToPoint)];

            var box = candidate.Rotated;
            var line = new PhotoCropper.Core.Models.RotatedBox(box.Center, new System.Drawing.SizeF((float)(box.Size.Width + 2 * outside), (float)(box.Size.Height + 2 * outside)), box.Angle);
            var lineGeometry = Polygon([.. line.GetVertices().Select(ToPoint)]);
            context.DrawGeometry(null, outlinePen, lineGeometry);
            context.DrawGeometry(null, boxPen, lineGeometry);

            // Handles, the delete badge and the rotation handle only on the selected box.
            if (index != SelectedIndex) continue;

            foreach (Point corner in corners)
            {
                Rect handle = new Rect(corner.X - handleHalf, corner.Y - handleHalf, handleHalf * 2, handleHalf * 2).Intersect(scanRect);
                if (handle.Width > 0 && handle.Height > 0)
                {
                    context.DrawRectangle(White, handleBorderPen, handle);
                }
            }

            Point badge = ToPoint(geometry.DeleteBadge);
            context.DrawEllipse(DeleteBadgeBrush, null, badge, radius, radius);
            context.DrawLine(crossPen, new Point(badge.X - crossHalf, badge.Y - crossHalf), new Point(badge.X + crossHalf, badge.Y + crossHalf));
            context.DrawLine(crossPen, new Point(badge.X + crossHalf, badge.Y - crossHalf), new Point(badge.X - crossHalf, badge.Y + crossHalf));

            Point rotation = ToPoint(geometry.RotationHandle);
            context.DrawLine(stemPen, ToPoint(geometry.TopMid), rotation);
            context.DrawEllipse(RotationBadgeBrush, badgeRingPen, rotation, radius, radius);
            DrawRotationArrow(context, arrowPen, rotation, arrowRadius, s);
        }
    }

    /// <summary>A circular arrow from 45° to 315° (clockwise, y down) with a head at its end, inside the rotation badge.</summary>
    private static void DrawRotationArrow(DrawingContext context, IPen pen, Point center, double radius, double scale)
    {
        const double startDeg = 45, endDeg = 315;
        static double Rad(double deg) => deg * Math.PI / 180.0;
        Point OnArc(double deg) => new(center.X + radius * Math.Cos(Rad(deg)), center.Y + radius * Math.Sin(Rad(deg)));

        var arc = new StreamGeometry();
        using (var ctx = arc.Open())
        {
            ctx.BeginFigure(OnArc(startDeg), false);
            ctx.ArcTo(OnArc(endDeg), new Size(radius, radius), 0, isLargeArc: true, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        context.DrawGeometry(null, pen, arc);

        // Arrow head along the tangent at the end of the arc.
        double end = Rad(endDeg);
        Point tip = OnArc(endDeg);
        double tx = -Math.Sin(end), ty = Math.Cos(end);
        double nx = -ty, ny = tx;
        double headLen = 12.0 * scale, headWidth = 8.0 * scale;
        Point wing1 = new(tip.X - headLen * tx + headWidth * nx, tip.Y - headLen * ty + headWidth * ny);
        Point wing2 = new(tip.X - headLen * tx - headWidth * nx, tip.Y - headLen * ty - headWidth * ny);
        context.DrawGeometry(White, null, Polygon([tip, wing1, wing2]));
    }

    private static Point ToPoint(System.Drawing.PointF p) => new(p.X, p.Y);

    private static StreamGeometry Polygon(Point[] points)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(points[0], true);
        for (int i = 1; i < points.Length; i++) ctx.LineTo(points[i]);
        ctx.EndFigure(true);
        return geometry;
    }
}
