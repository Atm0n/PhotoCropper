using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace PhotoCropper.Gui.Controls;

/// <summary>
/// A scan fitted into the available space, with the photos found on it outlined and numbered. View only: boxes are
/// edited in the Crops stage.
/// </summary>
internal sealed class ScanPreview : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<ScanPreview, Bitmap?>(nameof(Source));

    /// <summary>Each photo's corners, from 0 to 1 of the image's width and height.</summary>
    public static readonly StyledProperty<IReadOnlyList<Point[]>?> BoxesProperty =
        AvaloniaProperty.Register<ScanPreview, IReadOnlyList<Point[]>?>(nameof(Boxes));

    public static readonly StyledProperty<IBrush> BoxBrushProperty =
        AvaloniaProperty.Register<ScanPreview, IBrush>(nameof(BoxBrush), new SolidColorBrush(Color.Parse("#ff3333")));

    static ScanPreview()
    {
        AffectsRender<ScanPreview>(SourceProperty, BoxesProperty, BoxBrushProperty);
    }

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public IReadOnlyList<Point[]>? Boxes
    {
        get => GetValue(BoxesProperty);
        set => SetValue(BoxesProperty, value);
    }

    public IBrush BoxBrush
    {
        get => GetValue(BoxBrushProperty);
        set => SetValue(BoxBrushProperty, value);
    }

    /// <summary>Where the image is drawn inside <paramref name="bounds"/>: as large as fits, keeping its shape, centred.</summary>
    internal static Rect FitRect(Size image, Size bounds)
    {
        if (image.Width <= 0 || image.Height <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return default;
        double scale = Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
        double w = image.Width * scale, h = image.Height * scale;
        return new Rect((bounds.Width - w) / 2, (bounds.Height - h) / 2, w, h);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Source is not { } source) return;

        var target = FitRect(source.Size, Bounds.Size);
        if (target.Width <= 0) return;
        context.DrawImage(source, new Rect(source.Size), target);

        if (Boxes is not { Count: > 0 } boxes) return;
        var pen = new Pen(BoxBrush, 2.5);
        var labelBrush = BoxBrush;
        for (int i = 0; i < boxes.Count; i++)
        {
            var corners = boxes[i];
            if (corners.Length < 3) continue;
            var points = corners.Select(p => new Point(target.X + p.X * target.Width, target.Y + p.Y * target.Height)).ToArray();

            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(points[0], isFilled: false);
                for (int k = 1; k < points.Length; k++) g.LineTo(points[k]);
                g.EndFigure(isClosed: true);
            }
            context.DrawGeometry(null, pen, geometry);

            // The photo's number at its top-left corner, as in the Crops stage.
            var topLeft = points.OrderBy(p => p.Y + p.X).First();
            var text = new FormattedText($"{i + 1}", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                Typeface.Default, 13, Brushes.White);
            var badge = new Rect(topLeft.X - 2, topLeft.Y - 2, text.Width + 10, text.Height + 4);
            context.DrawRectangle(labelBrush, null, badge, 3, 3);
            context.DrawText(text, new Point(badge.X + 5, badge.Y + 2));
        }
    }
}
