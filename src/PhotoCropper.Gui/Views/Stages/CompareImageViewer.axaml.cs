using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PhotoCropper.Core.Models;

namespace PhotoCropper.Gui.Views.Stages;

/// <summary>
/// Shows a photo large, with zoom (mouse wheel, around the pointer) and pan (drag; right or middle drag always pans).
/// Compares before/after with a split divider: left of it shows <see cref="Before"/>, right shows <see cref="After"/>.
/// In painting mode, left-drag paints repair strokes instead; finished strokes are reported as fractions of the
/// photo's size through <see cref="StrokeCompleted"/>. <see cref="Trim"/> shades what the final photo leaves out; in
/// trimming mode its edges (or corners, two edges at once) can be dragged, reported through <see cref="TrimCompleted"/>.
/// </summary>
internal sealed partial class CompareImageViewer : UserControl
{
    public static readonly StyledProperty<Bitmap?> BeforeProperty =
        AvaloniaProperty.Register<CompareImageViewer, Bitmap?>(nameof(Before));

    public static readonly StyledProperty<Bitmap?> AfterProperty =
        AvaloniaProperty.Register<CompareImageViewer, Bitmap?>(nameof(After));

    public static readonly StyledProperty<bool> IsPaintingProperty =
        AvaloniaProperty.Register<CompareImageViewer, bool>(nameof(IsPainting));

    public static readonly StyledProperty<double> BrushSizeProperty =
        AvaloniaProperty.Register<CompareImageViewer, double>(nameof(BrushSize), 8);

    public static readonly StyledProperty<bool> ShowBeforeOnlyProperty =
        AvaloniaProperty.Register<CompareImageViewer, bool>(nameof(ShowBeforeOnly));

    public static readonly StyledProperty<bool> IsTrimmingProperty =
        AvaloniaProperty.Register<CompareImageViewer, bool>(nameof(IsTrimming));

    public static readonly StyledProperty<System.Drawing.RectangleF?> TrimProperty =
        AvaloniaProperty.Register<CompareImageViewer, System.Drawing.RectangleF?>(nameof(Trim));

    public static readonly DirectProperty<CompareImageViewer, double> ZoomProperty =
        AvaloniaProperty.RegisterDirect<CompareImageViewer, double>(nameof(Zoom), v => v.Zoom);

    private static readonly IBrush StrokeBrush = new SolidColorBrush(Color.FromArgb(150, 255, 60, 60));
    private const double MaxZoom = 8;
    private const double DividerGrab = 10;

    private enum DragMode { None, Pan, Divider, Paint, Trim }

    [Flags]
    internal enum TrimEdges { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

    /// <summary>How near (screen pixels) an edge of the trim the pointer must be to take it.</summary>
    private const double TrimGrab = 10;

    /// <summary>The smallest trim, as a share of the photo's width or height.</summary>
    private const double MinTrimShare = 0.05;

    private TrimEdges _trimEdges;
    private (double Left, double Top, double Right, double Bottom) _liveTrim;

    private double _scale = 1;
    private double _offsetX;
    private double _offsetY;
    private double _split = 0.5;
    private bool _userZoomed;
    private PixelSize _imageSize;
    private double _zoom = 1;

    private DragMode _drag;
    private Point _dragStart;
    private (double X, double Y) _offsetAtDragStart;
    private Polyline? _liveStroke;
    private readonly List<(double X, double Y)> _strokePoints = [];

    public CompareImageViewer()
    {
        InitializeComponent();
        pnlViewport.SizeChanged += (_, _) =>
        {
            if (_userZoomed) ApplyTransform();
            else FitToView();
        };
    }

    public Bitmap? Before
    {
        get => GetValue(BeforeProperty);
        set => SetValue(BeforeProperty, value);
    }

    public Bitmap? After
    {
        get => GetValue(AfterProperty);
        set => SetValue(AfterProperty, value);
    }

    /// <summary>Left-drag paints repair strokes; the split is hidden so you paint on After.</summary>
    public bool IsPainting
    {
        get => GetValue(IsPaintingProperty);
        set => SetValue(IsPaintingProperty, value);
    }

    /// <summary>Brush size 1–50: the radius is this many thousandths of the photo's width (as the view-model stores it).</summary>
    public double BrushSize
    {
        get => GetValue(BrushSizeProperty);
        set => SetValue(BrushSizeProperty, value);
    }

    /// <summary>The trim's edges can be dragged; the split is hidden.</summary>
    public bool IsTrimming
    {
        get => GetValue(IsTrimmingProperty);
        set => SetValue(IsTrimmingProperty, value);
    }

    /// <summary>The part the final photo keeps, as fractions of the photo's size; null when nothing is trimmed.</summary>
    public System.Drawing.RectangleF? Trim
    {
        get => GetValue(TrimProperty);
        set => SetValue(TrimProperty, value);
    }

    /// <summary>Called with the trim once an edge has been dragged, as fractions (0..1) of the photo's size.</summary>
    public Action<System.Drawing.RectangleF>? TrimCompleted { get; set; }

    /// <summary>Shows Before over the whole photo (while a key is held).</summary>
    public bool ShowBeforeOnly
    {
        get => GetValue(ShowBeforeOnlyProperty);
        set => SetValue(ShowBeforeOnlyProperty, value);
    }

    /// <summary>Screen pixels per photo pixel (1 = 100%).</summary>
    public double Zoom
    {
        get => _zoom;
        private set => SetAndRaise(ZoomProperty, ref _zoom, value);
    }

    /// <summary>Called with each finished stroke's points, as fractions (0..1) of the photo's width and height.</summary>
    public Action<IReadOnlyList<(double X, double Y)>>? StrokeCompleted { get; set; }

    private double BrushRadiusPixels => Math.Clamp(BrushSize, 1, 50) / 1000.0 * _imageSize.Width;

    private Bitmap? Shown => After ?? Before;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BeforeProperty || change.Property == AfterProperty)
        {
            OnImagesChanged(change.Property == AfterProperty);
        }
        else if (change.Property == IsPaintingProperty || change.Property == ShowBeforeOnlyProperty || change.Property == IsTrimmingProperty)
        {
            if (!IsPainting) ellBrushCursor.IsVisible = false;
            UpdateSplit();
            UpdateTrimOverlay();
        }
        else if (change.Property == TrimProperty && _drag != DragMode.Trim)
        {
            UpdateTrimOverlay();
        }
    }

    private void OnImagesChanged(bool afterChanged)
    {
        imgBefore.Source = Before;
        imgAfter.Source = Shown;

        PixelSize size = Shown?.PixelSize ?? default;
        if (size != _imageSize)
        {
            // Another photo (or the first image): start from "fit" again.
            _imageSize = size;
            pnlContent.Width = size.Width;
            pnlContent.Height = size.Height;
            cnvStrokes.Width = size.Width;
            cnvStrokes.Height = size.Height;
            ClearStrokes(keepLive: false);
            if (_drag == DragMode.Trim) _drag = DragMode.None; // another photo: the edge being dragged was the last one's
            magnifier.Hide();
            _userZoomed = false;
            FitToView();
            return;
        }

        // Same photo, updated render: the magnifier must not keep drawing from the old one, which is disposed now.
        if (Shown is { } shown) magnifier.ReplaceSource(shown);

        // Finished strokes are now part of the image.
        if (afterChanged) ClearStrokes(keepLive: true);
        UpdateSplit();
        UpdateTrimOverlay();
    }

    // ---- Zoom and pan ----

    private double FitScale
    {
        get
        {
            Size area = pnlViewport.Bounds.Size;
            if (_imageSize.Width == 0 || area.Width <= 0 || area.Height <= 0) return 1;
            return Math.Min(area.Width / _imageSize.Width, area.Height / _imageSize.Height);
        }
    }

    /// <summary>Takes the keyboard focus (so the editor's keys reach the view around it).</summary>
    public void FocusViewport() => pnlViewport.Focus();

    /// <summary>Shows the whole photo.</summary>
    public void FitToView()
    {
        if (_imageSize.Width == 0 || pnlViewport.Bounds.Width <= 0) return;
        _userZoomed = false;
        _scale = Math.Min(FitScale, MaxZoom);
        ApplyTransform();
    }

    /// <summary>One photo pixel per screen pixel, keeping the centre of the view.</summary>
    public void ZoomToActualSize() => ZoomAt(ViewCentre, 1);

    public void ZoomIn() => ZoomAt(ViewCentre, _scale * 1.25);

    public void ZoomOut() => ZoomAt(ViewCentre, _scale / 1.25);

    private Point ViewCentre => new(pnlViewport.Bounds.Width / 2, pnlViewport.Bounds.Height / 2);

    private void ZoomAt(Point anchor, double scale)
    {
        if (_imageSize.Width == 0) return;
        double min = Math.Min(FitScale, 1) * 0.5;
        scale = Math.Clamp(scale, min, MaxZoom);

        // Keep the photo point under the anchor where it is.
        _offsetX = anchor.X - (anchor.X - _offsetX) * scale / _scale;
        _offsetY = anchor.Y - (anchor.Y - _offsetY) * scale / _scale;
        _scale = scale;
        _userZoomed = true;
        ApplyTransform();
    }

    private void ApplyTransform()
    {
        Size area = pnlViewport.Bounds.Size;
        double width = _imageSize.Width * _scale;
        double height = _imageSize.Height * _scale;

        // Centre a photo smaller than the view; otherwise don't let it be dragged away from the edges.
        _offsetX = width <= area.Width ? (area.Width - width) / 2 : Math.Clamp(_offsetX, area.Width - width, 0);
        _offsetY = height <= area.Height ? (area.Height - height) / 2 : Math.Clamp(_offsetY, area.Height - height, 0);

        pnlContent.RenderTransform = new MatrixTransform(new Matrix(_scale, 0, 0, _scale, _offsetX, _offsetY));
        ellBrushCursor.StrokeThickness = 1.5 / _scale;
        Zoom = _scale;
        UpdateSplit();
        UpdateTrimOverlay();
    }

    private Point ToImage(Point screen) => new((screen.X - _offsetX) / _scale, (screen.Y - _offsetY) / _scale);

    private bool IsOnImage(Point image) =>
        image.X >= 0 && image.Y >= 0 && image.X <= _imageSize.Width && image.Y <= _imageSize.Height;

    // ---- Split ----

    private bool SplitVisible => !IsPainting && !IsTrimming && !ShowBeforeOnly && Before != null && _imageSize.Width > 0;

    private double DividerX => _split * pnlViewport.Bounds.Width;

    private void UpdateSplit()
    {
        bool split = SplitVisible;
        imgBefore.IsVisible = (split || ShowBeforeOnly) && Before != null;
        if (ShowBeforeOnly)
        {
            imgBefore.Clip = null;
        }
        else if (split)
        {
            double edge = Math.Clamp((DividerX - _offsetX) / _scale, 0, _imageSize.Width);
            imgBefore.Clip = new RectangleGeometry(new Rect(0, 0, edge, _imageSize.Height));
        }

        rectDivider.IsVisible = split;
        bdrDividerHandle.IsVisible = split;
        bdrBeforeLabel.IsVisible = split || ShowBeforeOnly;
        bdrAfterLabel.IsVisible = split;
        if (!split) return;

        double height = pnlViewport.Bounds.Height;
        rectDivider.Height = height;
        Canvas.SetLeft(rectDivider, DividerX - rectDivider.Width / 2);
        Canvas.SetTop(bdrDividerHandle, height / 2 - bdrDividerHandle.Height / 2);
        Canvas.SetLeft(bdrDividerHandle, DividerX - bdrDividerHandle.Width / 2);
    }

    private bool IsNearDivider(Point screen)
    {
        if (!SplitVisible) return false;
        double height = pnlViewport.Bounds.Height;
        bool onHandle = Math.Abs(screen.Y - height / 2) <= bdrDividerHandle.Height / 2 + 2
                        && Math.Abs(screen.X - DividerX) <= bdrDividerHandle.Width / 2 + 2;
        return onHandle || Math.Abs(screen.X - DividerX) <= DividerGrab / 2 + 2;
    }

    // ---- Pointer ----

    private void Viewport_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        pnlViewport.Focus();
        var point = e.GetCurrentPoint(pnlViewport);
        Point position = point.Position;
        if (_imageSize.Width == 0) return;

        if (point.Properties.IsRightButtonPressed || point.Properties.IsMiddleButtonPressed)
        {
            StartDrag(DragMode.Pan, position);
        }
        else if (point.Properties.IsLeftButtonPressed)
        {
            if (TrimEdgesAt(position) is var edges and not TrimEdges.None)
            {
                StartTrimDrag(edges);
            }
            else if (IsNearDivider(position))
            {
                StartDrag(DragMode.Divider, position);
            }
            else if (IsPainting && IsOnImage(ToImage(position)))
            {
                StartStroke(ToImage(position));
                _drag = DragMode.Paint;
            }
            else
            {
                StartDrag(DragMode.Pan, position);
            }
        }
        else
        {
            return;
        }

        e.Pointer.Capture(pnlViewport);
        e.Handled = true;
    }

    private void StartDrag(DragMode mode, Point position)
    {
        _drag = mode;
        _dragStart = position;
        _offsetAtDragStart = (_offsetX, _offsetY);
    }

    private void Viewport_PointerMoved(object? sender, PointerEventArgs e)
    {
        Point position = e.GetPosition(pnlViewport);
        Point image = ToImage(position);

        switch (_drag)
        {
            case DragMode.Pan:
                _offsetX = _offsetAtDragStart.X + position.X - _dragStart.X;
                _offsetY = _offsetAtDragStart.Y + position.Y - _dragStart.Y;
                ApplyTransform();
                break;
            case DragMode.Divider:
                _split = Math.Clamp(position.X / Math.Max(1, pnlViewport.Bounds.Width), 0, 1);
                UpdateSplit();
                break;
            case DragMode.Paint:
                ContinueStroke(image);
                break;
            case DragMode.Trim:
                MoveTrimEdges(image);
                ShowMagnifier(image, position);
                break;
        }

        MoveBrushCursor(image);
        pnlViewport.Cursor = _drag switch
        {
            DragMode.Pan => new Cursor(StandardCursorType.SizeAll),
            DragMode.Divider => new Cursor(StandardCursorType.SizeWestEast),
            DragMode.Paint => new Cursor(StandardCursorType.Cross),
            DragMode.Trim => TrimCursor(_trimEdges),
            _ when TrimEdgesAt(position) is var edges and not TrimEdges.None => TrimCursor(edges),
            _ when IsNearDivider(position) => new Cursor(StandardCursorType.SizeWestEast),
            _ when IsPainting && IsOnImage(image) => new Cursor(StandardCursorType.Cross),
            _ => Cursor.Default,
        };
    }

    private void Viewport_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_drag == DragMode.None) return;
        if (_drag == DragMode.Pan && (Math.Abs(_offsetX - _offsetAtDragStart.X) > 0 || Math.Abs(_offsetY - _offsetAtDragStart.Y) > 0))
        {
            _userZoomed = true; // the user moved the photo: keep it there when the window resizes
        }
        if (_drag == DragMode.Paint) FinishStroke();
        if (_drag == DragMode.Trim) FinishTrimDrag();
        _drag = DragMode.None;
        e.Pointer.Capture(null);
    }

    private void Viewport_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_drag == DragMode.Paint) FinishStroke();
        if (_drag == DragMode.Trim) FinishTrimDrag();
        _drag = DragMode.None;
    }

    private void Viewport_PointerExited(object? sender, PointerEventArgs e) => ellBrushCursor.IsVisible = false;

    private void Viewport_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_imageSize.Width == 0) return;
        ZoomAt(e.GetPosition(pnlViewport), _scale * Math.Pow(1.2, e.Delta.Y));
        e.Handled = true;
    }

    // ---- Trim (edges in photo pixels; the overlay is drawn in screen space so its lines stay thin) ----

    /// <summary>The trim in photo pixels: the one being dragged, or the one shown.</summary>
    private (double Left, double Top, double Right, double Bottom)? TrimPixels =>
        _drag == DragMode.Trim ? _liveTrim
        : Trim is { } t && _imageSize.Width > 0
            ? (t.Left * _imageSize.Width, t.Top * _imageSize.Height, t.Right * _imageSize.Width, t.Bottom * _imageSize.Height)
            : null;

    private void UpdateTrimOverlay()
    {
        bool show = !ShowBeforeOnly && TrimPixels != null;
        cnvTrim.IsVisible = show;
        if (!show) return;
        var (left, top, right, bottom) = TrimPixels!.Value;

        // The photo and the trim on screen.
        double ix0 = _offsetX, iy0 = _offsetY, ix1 = _offsetX + _imageSize.Width * _scale, iy1 = _offsetY + _imageSize.Height * _scale;
        double l = _offsetX + left * _scale, t = _offsetY + top * _scale, r = _offsetX + right * _scale, b = _offsetY + bottom * _scale;

        static void Place(Control c, double x, double y, double w, double h)
        {
            Canvas.SetLeft(c, x);
            Canvas.SetTop(c, y);
            c.Width = Math.Max(0, w);
            c.Height = Math.Max(0, h);
        }
        Place(rectShadeTop, ix0, iy0, ix1 - ix0, t - iy0);
        Place(rectShadeBottom, ix0, b, ix1 - ix0, iy1 - b);
        Place(rectShadeLeft, ix0, t, l - ix0, b - t);
        Place(rectShadeRight, r, t, ix1 - r, b - t);

        bool editing = IsTrimming;
        rectTrim.IsVisible = editing;
        bdrTrimLeft.IsVisible = bdrTrimRight.IsVisible = bdrTrimTop.IsVisible = bdrTrimBottom.IsVisible = editing;
        if (!editing) return;
        Place(rectTrim, l, t, r - l, b - t);
        double midX = (l + r) / 2, midY = (t + b) / 2;
        Canvas.SetLeft(bdrTrimLeft, l - bdrTrimLeft.Width / 2);
        Canvas.SetTop(bdrTrimLeft, midY - bdrTrimLeft.Height / 2);
        Canvas.SetLeft(bdrTrimRight, r - bdrTrimRight.Width / 2);
        Canvas.SetTop(bdrTrimRight, midY - bdrTrimRight.Height / 2);
        Canvas.SetLeft(bdrTrimTop, midX - bdrTrimTop.Width / 2);
        Canvas.SetTop(bdrTrimTop, t - bdrTrimTop.Height / 2);
        Canvas.SetLeft(bdrTrimBottom, midX - bdrTrimBottom.Width / 2);
        Canvas.SetTop(bdrTrimBottom, b - bdrTrimBottom.Height / 2);
    }

    /// <summary>The trim edges under the pointer (two near a corner); none outside trimming mode.</summary>
    private TrimEdges TrimEdgesAt(Point screen)
    {
        if (!IsTrimming || TrimPixels is not { } trim) return TrimEdges.None;
        var onScreen = new Rect(new Point(_offsetX + trim.Left * _scale, _offsetY + trim.Top * _scale),
            new Point(_offsetX + trim.Right * _scale, _offsetY + trim.Bottom * _scale));
        return EdgesNear(screen, onScreen, TrimGrab);
    }

    /// <summary>The edges of <paramref name="frame"/> within <paramref name="grab"/> of <paramref name="point"/> (two near a corner).</summary>
    internal static TrimEdges EdgesNear(Point point, Rect frame, double grab)
    {
        bool alongX = point.X >= frame.Left - grab && point.X <= frame.Right + grab;
        bool alongY = point.Y >= frame.Top - grab && point.Y <= frame.Bottom + grab;
        var edges = TrimEdges.None;
        if (alongY && Math.Abs(point.X - frame.Left) <= grab) edges |= TrimEdges.Left;
        else if (alongY && Math.Abs(point.X - frame.Right) <= grab) edges |= TrimEdges.Right;
        if (alongX && Math.Abs(point.Y - frame.Top) <= grab) edges |= TrimEdges.Top;
        else if (alongX && Math.Abs(point.Y - frame.Bottom) <= grab) edges |= TrimEdges.Bottom;
        return edges;
    }

    private static Cursor TrimCursor(TrimEdges edges) => new(edges switch
    {
        TrimEdges.Left | TrimEdges.Top or TrimEdges.Right | TrimEdges.Bottom => StandardCursorType.TopLeftCorner,
        TrimEdges.Right | TrimEdges.Top or TrimEdges.Left | TrimEdges.Bottom => StandardCursorType.TopRightCorner,
        TrimEdges.Left or TrimEdges.Right => StandardCursorType.SizeWestEast,
        _ => StandardCursorType.SizeNorthSouth,
    });

    private void StartTrimDrag(TrimEdges edges)
    {
        _liveTrim = TrimPixels!.Value;
        _trimEdges = edges;
        _drag = DragMode.Trim;
    }

    private void MoveTrimEdges(Point image)
    {
        var moved = MoveEdges(new Rect(new Point(_liveTrim.Left, _liveTrim.Top), new Point(_liveTrim.Right, _liveTrim.Bottom)),
            _trimEdges, image, new Size(_imageSize.Width, _imageSize.Height));
        _liveTrim = (moved.Left, moved.Top, moved.Right, moved.Bottom);
        UpdateTrimOverlay();
    }

    /// <summary>
    /// <paramref name="frame"/> with <paramref name="edges"/> moved to <paramref name="point"/> (photo pixels), kept on
    /// the photo and at least <see cref="MinTrimShare"/> of it wide and high.
    /// </summary>
    internal static Rect MoveEdges(Rect frame, TrimEdges edges, Point point, Size photo)
    {
        double minW = photo.Width * MinTrimShare, minH = photo.Height * MinTrimShare;
        double left = frame.Left, top = frame.Top, right = frame.Right, bottom = frame.Bottom;
        if (edges.HasFlag(TrimEdges.Left)) left = Math.Clamp(point.X, 0, right - minW);
        if (edges.HasFlag(TrimEdges.Right)) right = Math.Clamp(point.X, left + minW, photo.Width);
        if (edges.HasFlag(TrimEdges.Top)) top = Math.Clamp(point.Y, 0, bottom - minH);
        if (edges.HasFlag(TrimEdges.Bottom)) bottom = Math.Clamp(point.Y, top + minH, photo.Height);
        return new Rect(new Point(left, top), new Point(right, bottom));
    }

    private void FinishTrimDrag()
    {
        magnifier.Hide();
        var (left, top, right, bottom) = _liveTrim;
        double w = _imageSize.Width, h = _imageSize.Height;
        _drag = DragMode.None;
        TrimCompleted?.Invoke(new System.Drawing.RectangleF((float)(left / w), (float)(top / h), (float)((right - left) / w), (float)((bottom - top) / h)));
        UpdateTrimOverlay();
    }

    /// <summary>
    /// Shows the photo around the pointer, magnified, with the trim as a thin line, in a bottom corner of the view:
    /// the left one, or the right one when the pointer is near the first.
    /// </summary>
    private void ShowMagnifier(Point image, Point screen)
    {
        if (Shown is not { } bitmap) return;
        var (left, top, right, bottom) = _liveTrim;
        var trimBox = new CropCandidate { Rotated = new RotatedBox(new System.Drawing.PointF((float)((left + right) / 2), (float)((top + bottom) / 2)), new System.Drawing.SizeF((float)(right - left), (float)(bottom - top)), 0) };

        double reach = magnifier.Width + 2 * magnifier.Margin.Left + 40;
        double height = pnlViewport.Bounds.Height;
        if (screen.X < reach && screen.Y > height - reach) magnifier.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        else if (screen.X > pnlViewport.Bounds.Width - reach && screen.Y > height - reach) magnifier.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;

        // Four times the view's own zoom, up to 3 screen pixels per photo pixel (never less than the view).
        double zoom = Math.Max(_scale, Math.Min(_scale * 4, 3));
        magnifier.Show(bitmap, image, zoom, [trimBox], Colors.White);
    }

    // ---- Painting (in photo pixels, inside the transformed content, so strokes zoom with the photo) ----

    private void StartStroke(Point image)
    {
        _liveStroke = new Polyline
        {
            Stroke = StrokeBrush,
            StrokeThickness = Math.Max(1, BrushRadiusPixels * 2),
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
        cnvStrokes.Children.Add(_liveStroke);
        AddPoint(image);
    }

    private void ContinueStroke(Point image)
    {
        if (_liveStroke == null) return;
        Point last = _liveStroke.Points[^1];
        // Skip tiny moves (in screen pixels): they add points without changing the stroke.
        if ((Math.Abs(last.X - image.X) + Math.Abs(last.Y - image.Y)) * _scale < 2) return;
        AddPoint(image);
    }

    private void AddPoint(Point image)
    {
        var clamped = new Point(Math.Clamp(image.X, 0, _imageSize.Width), Math.Clamp(image.Y, 0, _imageSize.Height));
        _liveStroke!.Points.Add(clamped);
        if (_liveStroke.Points.Count == 1) _liveStroke.Points.Add(clamped); // a single point needs two to render as a dot
        _strokePoints.Add((clamped.X / _imageSize.Width, clamped.Y / _imageSize.Height));
    }

    private void FinishStroke()
    {
        if (_liveStroke == null) return;
        // A click without moving still paints a dot (the first point covers it).
        StrokeCompleted?.Invoke([.. _strokePoints]);
        _strokePoints.Clear();
        _liveStroke = null; // the line stays visible until the repaired image arrives
    }

    private void MoveBrushCursor(Point image)
    {
        bool show = IsPainting && (_drag is DragMode.None or DragMode.Paint) && IsOnImage(image);
        ellBrushCursor.IsVisible = show;
        if (!show) return;
        double diameter = Math.Max(4 / _scale, BrushRadiusPixels * 2);
        ellBrushCursor.Width = diameter;
        ellBrushCursor.Height = diameter;
        Canvas.SetLeft(ellBrushCursor, image.X - diameter / 2);
        Canvas.SetTop(ellBrushCursor, image.Y - diameter / 2);
    }

    /// <summary>
    /// Removes the painted lines once the repaired image arrives (it shows them now); optionally keeps the stroke
    /// being painted right now, since a render of an earlier stroke can arrive mid-stroke.
    /// </summary>
    private void ClearStrokes(bool keepLive)
    {
        for (int i = cnvStrokes.Children.Count - 1; i >= 0; i--)
        {
            if (cnvStrokes.Children[i] is Polyline line && !(keepLive && ReferenceEquals(line, _liveStroke)))
            {
                cnvStrokes.Children.RemoveAt(i);
            }
        }
        if (!keepLive || _liveStroke == null)
        {
            _liveStroke = null;
            _strokePoints.Clear();
            if (_drag == DragMode.Paint) _drag = DragMode.None;
        }
    }
}
