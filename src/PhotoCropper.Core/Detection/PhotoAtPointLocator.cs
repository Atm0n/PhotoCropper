using OpenCvSharp;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Extraction;
using PhotoCropper.Core.Models;
using DrawingPointF = System.Drawing.PointF;

namespace PhotoCropper.Core.Detection;

/// <summary>
/// Finds the photo under a point the user clicked on a scan. The click says a photo is there, which automatic
/// detection never knows, so it can try every detection setting (as auto-tune does) and keep only the shapes
/// that contain the point. The scan's other photos are painted over first, so the clicked photo can't merge
/// with them.
/// </summary>
public static class PhotoAtPointLocator
{
    /// <summary>The scan is searched at this size (longer side, pixels), as detection does.</summary>
    private const double WorkingSide = 1600;

    internal static readonly double[] Tolerances = [8, 12, 16, 20, 25, 35, 45];
    internal static readonly double[] CannyLows = [20, 50];

    /// <summary>Smallest and largest photo, as a fraction of the scan's area.</summary>
    private const double MinAreaFraction = 0.002;
    private const double MaxAreaFraction = 0.92;

    // Experiment knobs (read by the benchmark through environment variables; fixed once tuned).
    internal static double PhotoLikeFill = Env("PC_FILL", 0.90);
    internal static double FillSlack = Env("PC_SLACK", 0.04);
    internal static double SwallowFraction = Env("PC_SWALLOW", 0.5);
    internal static bool PaintOthers = Env("PC_PAINT", 1) > 0;
    internal static bool UseDetector = Env("PC_DETECTOR", 1) > 0;
    internal static bool UseOutlines = Env("PC_OUTLINES", 1) > 0;
    internal static int GrabCutMode = (int)Env("PC_GRABCUT", 0);      // 0 off, 1 refine the chosen shape
    internal static double GrabCutGrow = Env("PC_GC_GROW", 0.2);     // "probably photo" = chosen box grown by this
    internal static double GrabCutRing = Env("PC_GC_RING", 0.5);     // background beyond the box grown by this
    internal static double GrabCutSide = Env("PC_GC_SIDE", 700);     // GrabCut runs at this size (longer side)

    private static double Env(string name, double fallback) =>
        double.TryParse(Environment.GetEnvironmentVariable(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : fallback;

    /// <param name="scan">The scan, as loaded.</param>
    /// <param name="point">The clicked point, in scan pixels.</param>
    /// <param name="options">The scan's detection options (background colour and tolerance are used as hints).</param>
    /// <param name="otherBoxes">The scan's other photos: a result never covers them. Leave out the box being re-fitted.</param>
    /// <returns>The photo's box in scan pixels, or <see langword="null"/> when no photo was found at the point.</returns>
    public static RotatedBox? Locate(Mat scan, DrawingPointF point, DetectionOptions options, IReadOnlyList<RotatedBox> otherBoxes)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(otherBoxes);
        if (scan.Empty() || point.X < 0 || point.Y < 0 || point.X >= scan.Width || point.Y >= scan.Height) return null;

        // Painting over a dark scanner bezel can also paint over a dark photo touching the scan's edge, so a
        // search that finds nothing is repeated on the scan as it is.
        var found = Find(scan, point, options, otherBoxes, neutralizeBezels: true)
            ?? Find(scan, point, options, otherBoxes, neutralizeBezels: false);
        return found is { } box ? CvInterop.ToBox(PhotoExtractionEngine.RegularizeNearRightAngles(box)) : null;
    }

    /// <returns>The chosen box in scan pixels.</returns>
    private static RotatedRect? Find(Mat scan, DrawingPointF point, DetectionOptions options, IReadOnlyList<RotatedBox> otherBoxes, bool neutralizeBezels)
    {
        using var image = WorkingImage.Create(scan, options, otherBoxes, neutralizeBezels);
        var search = new Search(image, new Point2f((float)(point.X * image.Scale), (float)(point.Y * image.Scale)), otherBoxes);

        bool lightBackground = image.BackgroundHsv.Val2 > 120;
        using Mat foreground = new();
        foreach (double cannyLow in CannyLows)
        {
            double cannyHigh = cannyLow * AppConstants.DefaultCannyHighRatio;
            using Mat edges = ForegroundMaskGenerator.GeneratePrecomputedEdgeMap(image.Bgr, cannyLow, cannyHigh);
            foreach (double tolerance in Tolerances)
            {
                ForegroundMaskGenerator.PopulateForegroundMask(image.Bgr, foreground, image.BackgroundHsv, tolerance, cannyLow, cannyHigh, edges, image.Hsv);
                search.Consider(foreground);
            }
            ForegroundMaskGenerator.PopulateOtsuForegroundMask(image.Bgr, foreground, cannyLow, cannyHigh, lightBackground, edges);
            search.Consider(foreground);
        }

        var chosen = search.Choose();
        if (chosen is { } shape && GrabCutMode == 1)
        {
            chosen = GrabCutAround(image, search.Click, shape, search.Others) ?? shape;
        }
        return chosen is { } best ? Scale(best, 1.0 / image.Scale) : null;
    }

    /// <summary>
    /// Redraws the photo's outline with GrabCut, which learns the photo's and the surroundings' colours from the
    /// scan itself: the chosen box grown a little is "probably photo", the scan's other boxes and the area well
    /// outside are background, and the click is photo. Working-size scan coordinates in and out.
    /// </summary>
    private static RotatedRect? GrabCutAround(WorkingImage image, Point2f click, RotatedRect hint, Point2f[][] others)
    {
        double g = Math.Min(1.0, GrabCutSide / Math.Max(image.Bgr.Width, image.Bgr.Height));
        using Mat small = new();
        Cv2.Resize(image.Bgr, small, new Size(Math.Max(1, (int)Math.Round(image.Bgr.Width * g)), Math.Max(1, (int)Math.Round(image.Bgr.Height * g))), 0, 0, InterpolationFlags.Area);
        // Scan (working-size) coordinates to the small padded image and back.
        Point2f ToSmall(Point2f p) => new((float)((p.X + image.Pad) * g), (float)((p.Y + image.Pad) * g));
        Point[] Poly(RotatedRect r, double grow) =>
            [.. Grow(r, grow).Points().Select(p => ToSmall(p)).Select(p => new Point((int)Math.Round(p.X), (int)Math.Round(p.Y)))];

        using Mat mask = new(small.Size(), MatType.CV_8UC1, Scalar.All((int)GrabCutClasses.BGD));
        Cv2.FillConvexPoly(mask, Poly(hint, GrabCutGrow + GrabCutRing), Scalar.All(2)); // probably background
        Cv2.FillConvexPoly(mask, Poly(hint, GrabCutGrow), Scalar.All(3));               // probably photo
        foreach (var other in others)
        {
            Cv2.FillConvexPoly(mask, [.. other.Select(p => ToSmall(p)).Select(p => new Point((int)Math.Round(p.X), (int)Math.Round(p.Y)))], Scalar.All((int)GrabCutClasses.BGD));
        }
        var c = ToSmall(click);
        int seed = Math.Max(2, (int)(Math.Min(small.Width, small.Height) * 0.01));
        Cv2.Circle(mask, new Point((int)c.X, (int)c.Y), seed, Scalar.All((int)GrabCutClasses.FGD), -1);

        using Mat bgdModel = new();
        using Mat fgdModel = new();
        Cv2.GrabCut(small, mask, default, bgdModel, fgdModel, 4, GrabCutModes.InitWithMask);

        // Photo = definitely or probably foreground; keep the part containing the click.
        using Mat foreground = new();
        Cv2.InRange(mask, Scalar.All(1), Scalar.All(1), foreground);
        using Mat probable = new();
        Cv2.InRange(mask, Scalar.All(3), Scalar.All(3), probable);
        Cv2.BitwiseOr(foreground, probable, foreground);
        Cv2.FindContours(foreground, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        foreach (var contour in contours)
        {
            Point[] hull = Cv2.ConvexHull(contour);
            if (Cv2.PointPolygonTest(hull, c, false) < 0) continue;
            RotatedRect r = Cv2.MinAreaRect(hull);
            // Back to working-size scan coordinates.
            return new RotatedRect(new Point2f((float)(r.Center.X / g - image.Pad), (float)(r.Center.Y / g - image.Pad)),
                new Size2f((float)(r.Size.Width / g), (float)(r.Size.Height / g)), r.Angle);
        }
        return null;
    }

    private static RotatedRect Grow(RotatedRect r, double fraction)
    {
        float d = (float)(Math.Min(r.Size.Width, r.Size.Height) * fraction);
        return new RotatedRect(r.Center, new Size2f(r.Size.Width + 2 * d, r.Size.Height + 2 * d), r.Angle);
    }

    private static RotatedRect Scale(RotatedRect r, double s) =>
        new(new Point2f((float)(r.Center.X * s), (float)(r.Center.Y * s)), new Size2f((float)(r.Size.Width * s), (float)(r.Size.Height * s)), r.Angle);

    /// <summary>
    /// The scan as detection sees it: padded with the background colour (so photos running off the scan still
    /// have a closed outline), bezels and the other photos painted over, at working size.
    /// </summary>
    private sealed class WorkingImage : IDisposable
    {
        public required Mat Bgr { get; init; }
        public required Mat Hsv { get; init; }
        public required Scalar BackgroundHsv { get; init; }
        public required Scalar BackgroundBgr { get; init; }
        /// <summary>Working-size pixels per scan pixel.</summary>
        public required double Scale { get; init; }
        /// <summary>The padding, in working-size pixels.</summary>
        public required int Pad { get; init; }
        /// <summary>The scan's size at working size.</summary>
        public required Size ScanSize { get; init; }

        public static WorkingImage Create(Mat scan, DetectionOptions options, IReadOnlyList<RotatedBox> otherBoxes, bool neutralizeBezels)
        {
            using Mat original = PhotoCropperEngine.NormalizeToBgr(scan);
            Scalar backgroundHsv;
            using (Mat fullHsv = new())
            {
                Cv2.CvtColor(original, fullHsv, ColorConversionCodes.BGR2HSV);
                backgroundHsv = options.CustomBackgroundColorHsv is { } picked ? CvInterop.ToCv(picked) : BackgroundAnalyzer.SampleBackgroundColor(fullHsv);
            }
            Scalar backgroundBgr = BackgroundAnalyzer.HsvToBgr(backgroundHsv);

            int pad = Math.Max(20, Math.Min(original.Width, original.Height) / 50);
            using Mat padded = new();
            Cv2.CopyMakeBorder(original, padded, pad, pad, pad, pad, BorderTypes.Constant, backgroundBgr);

            if (neutralizeBezels)
            {
                var (top, bottom, left, right) = BackgroundAnalyzer.DetectBezelMargins(original, backgroundHsv, options.BackgroundTolerance);
                if (top > 0) Cv2.Rectangle(padded, new Rect(0, 0, padded.Width, pad + top), backgroundBgr, -1);
                if (bottom > 0) Cv2.Rectangle(padded, new Rect(0, padded.Height - pad - bottom, padded.Width, pad + bottom), backgroundBgr, -1);
                if (left > 0) Cv2.Rectangle(padded, new Rect(0, 0, pad + left, padded.Height), backgroundBgr, -1);
                if (right > 0) Cv2.Rectangle(padded, new Rect(padded.Width - pad - right, 0, pad + right, padded.Height), backgroundBgr, -1);
            }

            if (PaintOthers)
            {
                foreach (var other in otherBoxes)
                {
                    Point[] corners = [.. other.GetVertices().Select(p => new Point((int)Math.Round(p.X) + pad, (int)Math.Round(p.Y) + pad))];
                    Cv2.FillConvexPoly(padded, corners, backgroundBgr);
                }
            }

            double scale = Math.Min(1.0, WorkingSide / Math.Max(padded.Width, padded.Height));
            Mat bgr = new();
            if (scale < 0.999)
            {
                Cv2.Resize(padded, bgr, new Size((int)Math.Round(padded.Width * scale), (int)Math.Round(padded.Height * scale)), 0, 0, InterpolationFlags.Area);
            }
            else
            {
                padded.CopyTo(bgr);
            }
            Mat hsv = new();
            Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);

            return new WorkingImage
            {
                Bgr = bgr,
                Hsv = hsv,
                BackgroundHsv = backgroundHsv,
                BackgroundBgr = backgroundBgr,
                Scale = scale,
                Pad = (int)Math.Round(pad * scale),
                ScanSize = new Size((int)Math.Round(original.Width * scale), (int)Math.Round(original.Height * scale)),
            };
        }

        public void Dispose()
        {
            Bgr.Dispose();
            Hsv.Dispose();
        }
    }

    /// <summary>Collects the shapes containing the click (working-size scan coordinates) across foreground masks.</summary>
    private sealed class Search
    {
        private readonly WorkingImage _image;
        private readonly Point2f _click;
        private readonly Point2f[][] _others;

        public Point2f Click => _click;
        public Point2f[][] Others => _others;
        private readonly double _imageArea;
        private readonly List<(RotatedRect Box, double Fill, double Area)> _candidates = [];

        public Search(WorkingImage image, Point2f click, IReadOnlyList<RotatedBox> otherBoxes)
        {
            _image = image;
            _click = click;
            _imageArea = (double)image.ScanSize.Width * image.ScanSize.Height;
            _others = [.. otherBoxes.Select(b => Scale(CvInterop.ToCv(b), image.Scale).Points())];
        }

        public void Consider(Mat foreground)
        {
            if (UseDetector)
            {
                // Detection's own candidates for this mask, with composites (several photos as one) resolved.
                var candidates = CandidateExtractor.ExtractCandidates(
                    foreground, _image.Pad, _image.ScanSize.Width, _image.ScanSize.Height, MinAreaFraction, MaxAreaFraction, _image.Bgr, _image.BackgroundBgr);
                foreach (var candidate in CandidateResolutionFilter.FilterCandidates(candidates))
                {
                    var box = CvInterop.ToCv(candidate.Rotated);
                    if (Cv2.PointPolygonTest(box.Points(), _click, false) < 0) continue;
                    Add(box, candidate.Rectangularity);
                }
            }

            if (UseOutlines)
            {
                Cv2.FindContours(foreground, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                var padded = new Point2f(_click.X + _image.Pad, _click.Y + _image.Pad);
                foreach (var contour in contours)
                {
                    if (!Cv2.BoundingRect(contour).Contains(new Point((int)padded.X, (int)padded.Y))) continue;
                    Point[] hull = Cv2.ConvexHull(contour);
                    if (Cv2.PointPolygonTest(hull, padded, false) < 0) continue;

                    RotatedRect boxInPadded = Cv2.MinAreaRect(hull);
                    var box = new RotatedRect(new Point2f(boxInPadded.Center.X - _image.Pad, boxInPadded.Center.Y - _image.Pad), boxInPadded.Size, boxInPadded.Angle);
                    double boxArea = (double)box.Size.Width * box.Size.Height;
                    // How much of its box the shape fills: a photo fills it, merged neighbours or a ragged blob don't.
                    Add(box, boxArea > 0 ? Cv2.ContourArea(contour) / boxArea : 0);
                }
            }
        }

        private void Add(RotatedRect box, double fill)
        {
            double area = (double)box.Size.Width * box.Size.Height;
            double fraction = area / _imageArea;
            if (fraction < MinAreaFraction || fraction > MaxAreaFraction) return;
            if (SwallowsAnother(box, area)) return;
            _candidates.Add((box, fill, area));
        }

        public RotatedRect? Choose()
        {
            if (_candidates.Count == 0) return null;
            double bestFill = _candidates.Max(c => c.Fill);
            // The largest shape about as rectangular as the best one: a photo split by a colour close to the
            // background (or a print's white label) is found whole in some masks and in pieces in others.
            double threshold = Math.Max(Math.Min(PhotoLikeFill, bestFill), bestFill - FillSlack);
            return _candidates.Where(c => c.Fill >= threshold).MaxBy(c => c.Area).Box;
        }

        /// <summary>Whether the box covers most of another photo's box (or lies mostly inside one).</summary>
        private bool SwallowsAnother(RotatedRect box, double boxArea)
        {
            Point2f[] corners = box.Points();
            foreach (var other in _others)
            {
                double overlap = Cv2.IntersectConvexConvex(corners, other, out _);
                if (overlap <= 0) continue;
                double otherArea = Cv2.ContourArea(other);
                if (overlap > SwallowFraction * Math.Min(boxArea, otherArea)) return true;
            }
            return false;
        }
    }
}
