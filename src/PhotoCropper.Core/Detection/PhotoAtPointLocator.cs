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

    /// <summary>Among shapes about as rectangular as the best (within this much fill), the largest wins.</summary>
    private const double FillSlack = 0.04;
    /// <summary>A shape counts as photo-like when it fills at least this much of its box.</summary>
    private const double PhotoLikeFill = 0.90;
    /// <summary>A shape may cover at most this fraction of another photo's box (or its own area).</summary>
    private const double SwallowFraction = 0.5;
    /// <summary>A strip counts as background when at most this fraction of it is edges...</summary>
    private const double BackgroundEdgeDensity = 0.02;
    /// <summary>...and its brightness varies by at most this much (standard deviation, 0-255).</summary>
    private const double BackgroundStdDev = 10;
    /// <summary>A side moves out at most this much of the box's longer side.</summary>
    private const double GrowMax = 0.8;
    /// <summary>A photo found on an album page is at most this fraction of the page (else the "page" was a photo).</summary>
    private const double MaxPhotoOnPage = 0.6;
    /// <summary>Searched against the paper around it, a photo's box may grow at most this much (its border).</summary>
    private const double MaxPrintGrowth = 1.8;
    /// <summary>A print's white border is at most this fraction of its picture's shorter side.</summary>
    private const double MaxBorderShare = 0.2;
    /// <summary>Two strips' mean colours (BGR) this close or closer count as the same colour.</summary>
    private const double PaperDistance = 25;

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
        var found = Find(scan, point, options, otherBoxes, neutralizeBezels: true, onPage: null, out var paper, out var surroundings, out var pageBox);
        if (found == null && paper == null)
        {
            found = Find(scan, point, options, otherBoxes, neutralizeBezels: false, onPage: null, out paper, out surroundings, out pageBox);
        }

        // An album page (photos stuck on coloured paper) on the scanner bed is itself a clean rectangle around the
        // click. Searched again with the paper as the background, the photo stands out from the paper instead.
        bool onPaperFound = false;
        if (paper is { } paperHsv)
        {
            var onPaper = options with { CustomBackgroundColorHsv = CvInterop.ToColor(paperHsv) };
            var inner = Find(scan, point, onPaper, otherBoxes, neutralizeBezels: false, pageBox, out _, out _, out _);
            if (inner is { } photo && (found is not { } page || Area(photo) < MaxPhotoOnPage * Area(page)))
            {
                found = photo;
                onPaperFound = true;
            }
        }
        // A photo on an album page whose border is close to the scanner bed's colour stops at its picture. Against
        // the page's paper the whole print stands out, border included.
        if (!onPaperFound && found is { } picture && surroundings is { } paperAround)
        {
            var onPaper = options with { CustomBackgroundColorHsv = CvInterop.ToColor(paperAround) };
            var print = Find(scan, point, onPaper, otherBoxes, neutralizeBezels: false, onPage: null, out _, out _, out _);
            if (print is { } whole && Area(whole) >= 1.02 * Area(picture) && Area(whole) <= MaxPrintGrowth * Area(picture))
            {
                found = whole;
            }
            else
            {
                // That search can stop at the picture again (its border and the bed beyond the paper look alike).
                // The border is then the smooth band between the picture and the paper: grow over it to the paper.
                using var image = WorkingImage.Create(scan, options, otherBoxes, neutralizeBezels: false, onPage: null);
                if (GrowOverBorder(image, Scale(picture, image.Scale), paperAround) is { } bordered) found = Scale(bordered, 1.0 / image.Scale);
            }
        }
        return found is { } box ? CvInterop.ToBox(PhotoExtractionEngine.RegularizeNearRightAngles(box)) : null;
    }

    /// <param name="paper">When the box found looks like an album page rather than a photo, the page's colour (HSV).</param>
    /// <param name="surroundings">When the box's surroundings are one colour other than the background's (the paper
    /// of an album page the photo is stuck on), that colour (HSV).</param>
    /// <returns>The chosen box in scan pixels.</returns>
    /// <param name="onPage">When searching against an album page's paper: the page (scan pixels). Everything outside it
    /// is painted as paper, so a photo reaching the page's edge doesn't merge with the scanner bed beyond.</param>
    /// <param name="page">When <paramref name="paper"/> is set, the page it was found on (scan pixels).</param>
    private static RotatedRect? Find(Mat scan, DrawingPointF point, DetectionOptions options, IReadOnlyList<RotatedBox> otherBoxes, bool neutralizeBezels, RotatedRect? onPage, out Scalar? paper, out Scalar? surroundings, out RotatedRect? page)
    {
        paper = null;
        surroundings = null;
        page = null;
        using var image = WorkingImage.Create(scan, options, otherBoxes, neutralizeBezels, onPage);
        // On a page, the photo is well within it: the page's own outline is never the answer.
        double maxArea = onPage is { } within ? MaxPhotoOnPage * Area(Scale(within, image.Scale)) : double.MaxValue;
        var search = new Search(image, new Point2f((float)(point.X * image.Scale), (float)(point.Y * image.Scale)), otherBoxes, maxArea);

        bool lightBackground = image.BackgroundHsv.Val2 > 120;
        // Every Canny setting's colour masks then its Otsu mask. The masks are independent, so they are made and
        // outlined side by side; their shapes are then added in this order, so the choice is the same as when
        // they ran one after another.
        var edges = new Mat[CannyLows.Length];
        try
        {
            Parallel.For(0, CannyLows.Length, c =>
                edges[c] = ForegroundMaskGenerator.GeneratePrecomputedEdgeMap(image.Bgr, CannyLows[c], CannyLows[c] * AppConstants.DefaultCannyHighRatio));
            int masksPerCanny = Tolerances.Length + 1;
            var shapes = new List<(RotatedRect Box, double Fill)>[CannyLows.Length * masksPerCanny];
            Parallel.For(0, shapes.Length, m =>
            {
                int c = m / masksPerCanny, t = m % masksPerCanny;
                double cannyLow = CannyLows[c], cannyHigh = cannyLow * AppConstants.DefaultCannyHighRatio;
                using Mat foreground = new();
                if (t < Tolerances.Length)
                {
                    ForegroundMaskGenerator.PopulateForegroundMask(image.Bgr, foreground, image.BackgroundHsv, Tolerances[t], cannyLow, cannyHigh, edges[c], image.Hsv);
                }
                else
                {
                    ForegroundMaskGenerator.PopulateOtsuForegroundMask(image.Bgr, foreground, cannyLow, cannyHigh, lightBackground, edges[c]);
                }
                shapes[m] = search.ShapesAroundClick(foreground);
            });
            foreach (var mask in shapes)
            {
                foreach (var (box, fill) in mask) search.Add(box, fill);
            }
        }
        finally
        {
            foreach (var map in edges) map?.Dispose();
        }

        var chosen = search.Choose();
        if (chosen is { } shape)
        {
            chosen = GrowToBackground(image, shape, search.Others);
            paper = PageColour(image, chosen.Value);
            if (paper != null) page = Scale(chosen.Value, 1.0 / image.Scale);
            surroundings = SurroundingColour(image, chosen.Value, search.Others);
        }
        else if (search.LargestCovering is { } covering)
        {
            // Nothing but shapes covering the other photos: the clicked photo merged with the page they are on.
            paper = PageColour(image, covering);
            if (paper != null) page = Scale(covering, 1.0 / image.Scale);
        }
        return chosen is { } best ? Scale(best, 1.0 / image.Scale) : null;
    }

    /// <summary>
    /// Moves each side of the box outwards while the strip just outside it isn't background. A part of the photo
    /// close to the background's colour (a pink roof on a pink album page) is left out of every colour mask, but
    /// it has texture, which paper and the scanner bed don't: a strip counts as background when it is smooth.
    /// A side stops at another photo's box. Working-size scan coordinates in and out.
    /// </summary>
    private static RotatedRect GrowToBackground(WorkingImage image, RotatedRect box, Point2f[][] others)
    {
        float w = box.Size.Width, h = box.Size.Height;
        if (w < 8 || h < 8) return box;
        int margin = (int)Math.Ceiling(Math.Max(w, h) * GrowMax) + 4;
        int pw = (int)Math.Round(w) + 2 * margin, ph = (int)Math.Round(h) + 2 * margin;

        // Straighten the box and its surroundings: the box is axis-aligned at [margin, margin + w] x [margin, margin + h].
        var centre = new Point2f(box.Center.X + image.Pad, box.Center.Y + image.Pad);
        using Mat rotation = Cv2.GetRotationMatrix2D(centre, box.Angle, 1.0);
        rotation.Set<double>(0, 2, rotation.Get<double>(0, 2) - centre.X + pw / 2.0);
        rotation.Set<double>(1, 2, rotation.Get<double>(1, 2) - centre.Y + ph / 2.0);

        using Mat gray = new();
        Cv2.CvtColor(image.Bgr, gray, ColorConversionCodes.BGR2GRAY);
        using Mat edges = new();
        using (Mat blurred = new())
        {
            Cv2.GaussianBlur(gray, blurred, new Size(3, 3), 0);
            Cv2.Canny(blurred, edges, 30, 90);
        }
        using Mat othersMask = new(gray.Size(), MatType.CV_8UC1, Scalar.All(0));
        foreach (var other in others)
        {
            Cv2.FillConvexPoly(othersMask, [.. other.Select(p => new Point((int)Math.Round(p.X + image.Pad), (int)Math.Round(p.Y + image.Pad)))], Scalar.All(255));
        }

        using Mat grayPatch = new();
        using Mat edgePatch = new();
        using Mat otherPatch = new();
        var size = new Size(pw, ph);
        Cv2.WarpAffine(gray, grayPatch, rotation, size, InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(0));
        Cv2.WarpAffine(edges, edgePatch, rotation, size, InterpolationFlags.Nearest, BorderTypes.Constant, Scalar.All(0));
        // Outside the scan (and its padding) is treated as another photo: nothing to grow into.
        Cv2.WarpAffine(othersMask, otherPatch, rotation, size, InterpolationFlags.Nearest, BorderTypes.Constant, Scalar.All(255));

        int step = Math.Max(2, (int)Math.Round(Math.Min(w, h) * 0.015));
        double left = margin, right = margin + w, top = margin, bottom = margin + h;
        left -= Extend(grayPatch, edgePatch, otherPatch, step, margin, (int)top, (int)bottom, horizontal: true, from: (int)left, outward: -1);
        right += Extend(grayPatch, edgePatch, otherPatch, step, margin, (int)top, (int)bottom, horizontal: true, from: (int)right, outward: 1);
        top -= Extend(grayPatch, edgePatch, otherPatch, step, margin, (int)left, (int)right, horizontal: false, from: (int)top, outward: -1);
        bottom += Extend(grayPatch, edgePatch, otherPatch, step, margin, (int)left, (int)right, horizontal: false, from: (int)bottom, outward: 1);

        // Back to scan coordinates: undo the straightening.
        double rad = box.Angle * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
        double dx = (left + right) / 2 - pw / 2.0, dy = (top + bottom) / 2 - ph / 2.0;
        var c = new Point2f((float)(box.Center.X + dx * cos - dy * sin), (float)(box.Center.Y + dx * sin + dy * cos));
        return new RotatedRect(c, new Size2f((float)(right - left), (float)(bottom - top)), box.Angle);
    }

    /// <summary>
    /// The colour of the box's margin when it looks like an album page: a smooth strip of one colour just inside
    /// every side, unlike the background around it. A photo has its picture there (or a thin white border, which
    /// isn't followed by more of the same, and is usually the scanner bed's colour anyway).
    /// </summary>
    private static Scalar? PageColour(WorkingImage image, RotatedRect box)
    {
        int w = (int)Math.Round(box.Size.Width), h = (int)Math.Round(box.Size.Height);
        if (w < 40 || h < 40) return null;
        var centre = new Point2f(box.Center.X + image.Pad, box.Center.Y + image.Pad);
        using Mat rotation = Cv2.GetRotationMatrix2D(centre, box.Angle, 1.0);
        rotation.Set<double>(0, 2, rotation.Get<double>(0, 2) - centre.X + w / 2.0);
        rotation.Set<double>(1, 2, rotation.Get<double>(1, 2) - centre.Y + h / 2.0);
        using Mat patch = new();
        Cv2.WarpAffine(image.Bgr, patch, rotation, new Size(w, h), InterpolationFlags.Linear, BorderTypes.Replicate);
        using Mat hsv = new();
        Cv2.CvtColor(patch, hsv, ColorConversionCodes.BGR2HSV);
        using Mat gray = new();
        Cv2.CvtColor(patch, gray, ColorConversionCodes.BGR2GRAY);
        using Mat edges = new();
        using (Mat blurred = new())
        {
            Cv2.GaussianBlur(gray, blurred, new Size(3, 3), 0);
            Cv2.Canny(blurred, edges, 30, 90);
        }

        // Each side in segments: the first smooth strip (not the scanner bed's colour) within the outer 12% of the
        // box. Photos can reach the page's edge along part of a side, so a side counts as paper when two of its
        // five segments show it.
        int shorter = Math.Min(w, h);
        int thickness = Math.Max(3, shorter / 150);
        const int segments = 5;
        var colours = new List<Scalar>();
        for (int side = 0; side < 4; side++)
        {
            bool vertical = side >= 2;
            int length = vertical ? h : w;
            var sideColours = new List<Scalar>();
            for (int k = 0; k < segments; k++)
            {
                int from = length * k / segments, to = length * (k + 1) / segments;
                for (int depth = 2; depth + thickness <= shorter * 0.12; depth += thickness)
                {
                    Rect strip = side switch
                    {
                        0 => new Rect(from, depth, to - from, thickness),
                        1 => new Rect(from, h - depth - thickness, to - from, thickness),
                        2 => new Rect(depth, from, thickness, to - from),
                        _ => new Rect(w - depth - thickness, from, thickness, to - from),
                    };
                    using Mat e = new(edges, strip);
                    if (Cv2.CountNonZero(e) > BackgroundEdgeDensity * strip.Width * strip.Height) continue;
                    using Mat g = new(gray, strip);
                    Cv2.MeanStdDev(g, out _, out Scalar spread);
                    if (spread.Val0 > BackgroundStdDev) continue;
                    using Mat c = new(hsv, strip);
                    var mean = Cv2.Mean(c);
                    if (IsBackgroundColour(mean, image.BackgroundHsv)) continue; // a sliver of scanner bed inside the box
                    sideColours.Add(mean);
                    break;
                }
            }
            if (sideColours.Count >= 2) colours.AddRange(sideColours);
            else colours.Add(new Scalar(-1)); // marks a side without paper
        }
        int sidesWithPaper = 4 - colours.Count(c => c.Val0 < 0);
        colours.RemoveAll(c => c.Val0 < 0);
        // Paper on at least three sides: an old page can be torn or trimmed along one.
        if (sidesWithPaper < 3) return null;

        // One paper colour all round, give or take its ageing and a shadow or two: most of the samples close to their
        // median. (The background's colour was skipped, so a white-bordered photo on a white bed has no "paper".)
        if (MainColour(colours) == null) return null;

        // It's a page. Its colour comes from smooth patches all over it (margins and the gaps between photos): an old
        // page's edges are darker than the rest, and searching against the edges' colour would find the whole page.
        int cell = Math.Max(6, shorter / 40);
        var patches = new List<Scalar>();
        for (int y = 0; y + cell <= h; y += cell)
        {
            for (int x = 0; x + cell <= w; x += cell)
            {
                var r = new Rect(x, y, cell, cell);
                using Mat e = new(edges, r);
                if (Cv2.CountNonZero(e) > 0) continue;
                using Mat g = new(gray, r);
                Cv2.MeanStdDev(g, out _, out Scalar spread);
                if (spread.Val0 > BackgroundStdDev / 2) continue;
                using Mat c = new(hsv, r);
                var mean = Cv2.Mean(c);
                if (!IsBackgroundColour(mean, image.BackgroundHsv)) patches.Add(mean);
            }
        }
        return MainColour(patches) ?? MainColour(colours);
    }

    /// <summary>The samples' colour (the average of those close to their median) when at least 60% are close to it.</summary>
    private static Scalar? MainColour(List<Scalar> colours)
    {
        if (colours.Count == 0) return null;
        static double Median(IEnumerable<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[sorted.Count / 2];
        }
        var median = new Scalar(Median(colours.Select(c => c.Val0)), Median(colours.Select(c => c.Val1)), Median(colours.Select(c => c.Val2)));
        var close = colours.Where(c => Math.Abs(c.Val1 - median.Val1) <= 25 && Math.Abs(c.Val2 - median.Val2) <= 25).ToList();
        if (close.Count < 0.6 * colours.Count) return null;
        return new Scalar(close.Average(c => c.Val0), close.Average(c => c.Val1), close.Average(c => c.Val2));
    }

    /// <summary>
    /// The colour of the box's surroundings when they are one colour other than the background's: smooth strips a
    /// little way out from each side (past a print's own border), on at least three sides, away from other photos.
    /// </summary>
    private static Scalar? SurroundingColour(WorkingImage image, RotatedRect box, Point2f[][] others)
    {
        int w = (int)Math.Round(box.Size.Width), h = (int)Math.Round(box.Size.Height);
        if (w < 20 || h < 20) return null;
        int shorter = Math.Min(w, h);
        int margin = Math.Max(8, shorter / 4);
        int pw = w + 2 * margin, ph = h + 2 * margin;
        var centre = new Point2f(box.Center.X + image.Pad, box.Center.Y + image.Pad);
        using Mat rotation = Cv2.GetRotationMatrix2D(centre, box.Angle, 1.0);
        rotation.Set<double>(0, 2, rotation.Get<double>(0, 2) - centre.X + pw / 2.0);
        rotation.Set<double>(1, 2, rotation.Get<double>(1, 2) - centre.Y + ph / 2.0);
        var size = new Size(pw, ph);

        using Mat patch = new();
        Cv2.WarpAffine(image.Bgr, patch, rotation, size, InterpolationFlags.Linear, BorderTypes.Replicate);
        using Mat othersMask = new(image.Bgr.Size(), MatType.CV_8UC1, Scalar.All(0));
        foreach (var other in others)
        {
            Cv2.FillConvexPoly(othersMask, [.. other.Select(p => new Point((int)Math.Round(p.X + image.Pad), (int)Math.Round(p.Y + image.Pad)))], Scalar.All(255));
        }
        using Mat otherPatch = new();
        Cv2.WarpAffine(othersMask, otherPatch, rotation, size, InterpolationFlags.Nearest, BorderTypes.Constant, Scalar.All(255));
        using Mat hsv = new();
        Cv2.CvtColor(patch, hsv, ColorConversionCodes.BGR2HSV);
        using Mat gray = new();
        Cv2.CvtColor(patch, gray, ColorConversionCodes.BGR2GRAY);
        using Mat edges = new();
        using (Mat blurred = new())
        {
            Cv2.GaussianBlur(gray, blurred, new Size(3, 3), 0);
            Cv2.Canny(blurred, edges, 30, 90);
        }

        // Per side, the outermost smooth strip within the margin: past the print's border, on the paper.
        int thickness = Math.Max(3, shorter / 60);
        var colours = new List<Scalar>();
        for (int side = 0; side < 4; side++)
        {
            Scalar? found = null;
            for (int depth = margin - thickness; depth >= thickness; depth -= thickness)
            {
                Rect strip = side switch
                {
                    0 => new Rect(margin + w / 10, margin - depth - thickness, w - w / 5, thickness),
                    1 => new Rect(margin + w / 10, margin + h + depth, w - w / 5, thickness),
                    2 => new Rect(margin - depth - thickness, margin + h / 10, thickness, h - h / 5),
                    _ => new Rect(margin + w + depth, margin + h / 10, thickness, h - h / 5),
                };
                strip = strip.Intersect(new Rect(0, 0, pw, ph));
                if (strip.Width <= 0 || strip.Height <= 0) continue;
                using Mat o = new(otherPatch, strip);
                if (Cv2.CountNonZero(o) > 0) continue;
                using Mat e = new(edges, strip);
                if (Cv2.CountNonZero(e) > BackgroundEdgeDensity * strip.Width * strip.Height) continue;
                using Mat g = new(gray, strip);
                Cv2.MeanStdDev(g, out _, out Scalar spread);
                if (spread.Val0 > BackgroundStdDev) continue;
                using Mat c = new(hsv, strip);
                found = Cv2.Mean(c);
                break;
            }
            if (found is { } colour) colours.Add(colour);
        }
        if (colours.Count < 3) return null;
        return MainColour(colours) is { } around && !IsBackgroundColour(around, image.BackgroundHsv) ? around : null;
    }

    /// <summary>
    /// A print's border around the picture found: on every side, a smooth band of one colour (not the paper's) that
    /// ends at the paper around it. Each side moves out to where the paper starts; when a side doesn't end at paper
    /// within <see cref="MaxBorderShare"/> of the picture's shorter side, there is no border and nothing changes.
    /// Working-size scan coordinates in and out.
    /// </summary>
    private static RotatedRect? GrowOverBorder(WorkingImage image, RotatedRect picture, Scalar paperHsv)
    {
        int w = (int)Math.Round(picture.Size.Width), h = (int)Math.Round(picture.Size.Height);
        if (w < 20 || h < 20) return null;
        int limit = (int)Math.Ceiling(Math.Min(w, h) * MaxBorderShare) + 2;
        int pw = w + 2 * limit, ph = h + 2 * limit;
        var centre = new Point2f(picture.Center.X + image.Pad, picture.Center.Y + image.Pad);
        using Mat rotation = Cv2.GetRotationMatrix2D(centre, picture.Angle, 1.0);
        rotation.Set<double>(0, 2, rotation.Get<double>(0, 2) - centre.X + pw / 2.0);
        rotation.Set<double>(1, 2, rotation.Get<double>(1, 2) - centre.Y + ph / 2.0);
        using Mat patch = new();
        Cv2.WarpAffine(image.Bgr, patch, rotation, new Size(pw, ph), InterpolationFlags.Linear, BorderTypes.Replicate);

        Scalar paper = BackgroundAnalyzer.HsvToBgr(paperHsv);
        int step = Math.Max(2, Math.Min(w, h) / 100);
        var grown = new int[4];
        for (int side = 0; side < 4; side++)
        {
            int? width = BorderWidth(patch, side, w, h, limit, step, paper);
            if (width is not { } border || border == 0) return null;
            grown[side] = border;
        }

        // Back to scan coordinates: the straightened box was at [limit, limit + w] x [limit, limit + h].
        double left = limit - grown[2], right = limit + w + grown[3], top = limit - grown[0], bottom = limit + h + grown[1];
        double rad = picture.Angle * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
        double dx = (left + right) / 2 - pw / 2.0, dy = (top + bottom) / 2 - ph / 2.0;
        var c = new Point2f((float)(picture.Center.X + dx * cos - dy * sin), (float)(picture.Center.Y + dx * sin + dy * cos));
        return new RotatedRect(c, new Size2f((float)(right - left), (float)(bottom - top)), picture.Angle);
    }

    /// <summary>
    /// How wide one side's border is: strips of width <paramref name="step"/> outward from the picture's side
    /// (0 top, 1 bottom, 2 left, 3 right) are the border while smooth and of the first strip's colour; the border
    /// ends where a strip is the paper's colour (one strip across the border's own edge may be neither).
    /// <see langword="null"/> when the paper isn't reached within <paramref name="limit"/>.
    /// </summary>
    private static int? BorderWidth(Mat patch, int side, int w, int h, int limit, int step, Scalar paper)
    {
        Scalar? border = null;
        bool crossing = false;
        // Skip the picture's own edge, then look along the side's middle, away from the corners.
        for (int depth = 2; depth + step <= limit; depth += step)
        {
            Rect strip = side switch
            {
                0 => new Rect(limit + w / 10, limit - depth - step, w - w / 5, step),
                1 => new Rect(limit + w / 10, limit + h + depth, w - w / 5, step),
                2 => new Rect(limit - depth - step, limit + h / 10, step, h - h / 5),
                _ => new Rect(limit + w + depth, limit + h / 10, step, h - h / 5),
            };
            using Mat s = new(patch, strip);
            Cv2.MeanStdDev(s, out Scalar mean, out Scalar spread);
            if (Distance(mean, paper) <= PaperDistance) return border == null ? 0 : depth;
            bool smooth = Math.Max(spread.Val0, Math.Max(spread.Val1, spread.Val2)) <= BackgroundStdDev;
            if (border == null)
            {
                if (!smooth) return 0; // the picture goes on, or no border: nothing to grow over
                border = mean;
            }
            else if (!smooth || Distance(mean, border.Value) > PaperDistance)
            {
                if (crossing) return null; // something other than a border, then paper
                crossing = true;
            }
        }
        return null;
    }

    private static double Distance(Scalar a, Scalar b) =>
        Math.Sqrt((a.Val0 - b.Val0) * (a.Val0 - b.Val0) + (a.Val1 - b.Val1) * (a.Val1 - b.Val1) + (a.Val2 - b.Val2) * (a.Val2 - b.Val2));

    private static bool IsBackgroundColour(Scalar hsv, Scalar background) =>
        Math.Abs(hsv.Val1 - background.Val1) <= 15 && Math.Abs(hsv.Val2 - background.Val2) <= 20;

    /// <summary>
    /// How far one side moves out: strips of width <paramref name="step"/> beyond it are tried until one is
    /// background. If none is within the limit, or another photo comes first, the side stays (or stops there).
    /// </summary>
    private static int Extend(Mat gray, Mat edges, Mat others, int step, int limit, int spanFrom, int spanTo, bool horizontal, int from, int outward)
    {
        // Leave out the ends of the side, where the neighbouring sides' surroundings are.
        int inset = (spanTo - spanFrom) / 20;
        int a = spanFrom + inset, b = spanTo - inset;
        if (b - a < 4) return 0;
        // The strip right next to the side holds the side's own edge; start just past it.
        for (int moved = 0; moved + step <= limit; moved += step)
        {
            int near = from + outward * (moved + 2);
            int far = near + outward * step;
            int lo = Math.Min(near, far), hi = Math.Max(near, far);
            Rect strip = horizontal ? new Rect(lo, a, hi - lo, b - a) : new Rect(a, lo, b - a, hi - lo);
            strip = strip.Intersect(new Rect(0, 0, gray.Width, gray.Height));
            if (strip.Width <= 0 || strip.Height <= 0) return moved;

            using Mat otherStrip = new(others, strip);
            if (Cv2.CountNonZero(otherStrip) > 0.1 * strip.Width * strip.Height) return moved;

            using Mat edgeStrip = new(edges, strip);
            using Mat grayStrip = new(gray, strip);
            double density = Cv2.CountNonZero(edgeStrip) / (double)(strip.Width * strip.Height);
            Cv2.MeanStdDev(grayStrip, out _, out Scalar spread);
            if (density <= BackgroundEdgeDensity && spread.Val0 <= BackgroundStdDev) return moved;
        }
        return 0; // never reached background: the side was probably right after all
    }

    private static double Area(RotatedRect r) => (double)r.Size.Width * r.Size.Height;

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

        public static WorkingImage Create(Mat scan, DetectionOptions options, IReadOnlyList<RotatedBox> otherBoxes, bool neutralizeBezels, RotatedRect? onPage)
        {
            using Mat original = ImageIo.NormalizeToBgr(scan);
            Scalar backgroundHsv;
            if (options.CustomBackgroundColorHsv is { } picked)
            {
                backgroundHsv = CvInterop.ToCv(picked);
            }
            else
            {
                using Mat fullHsv = new();
                Cv2.CvtColor(original, fullHsv, ColorConversionCodes.BGR2HSV);
                // The scan's other photos are known: keep them out of the background sample.
                backgroundHsv = BackgroundAnalyzer.SampleBackgroundColor(fullHsv, [.. otherBoxes.Select(b => CvInterop.ToCv(b).Points())]);
            }
            Scalar backgroundBgr = BackgroundAnalyzer.HsvToBgr(backgroundHsv);

            if (onPage is { } page)
            {
                // Searching on an album page: everything off the page (the scanner bed) is background too, and so is a thin
                // band along the page's edge, whose outline would otherwise enclose (and fill in) the whole page.
                float band = Math.Min(page.Size.Width, page.Size.Height) * 0.015f;
                var inside = new RotatedRect(page.Center, new Size2f(page.Size.Width - 2 * band, page.Size.Height - 2 * band), page.Angle);
                using Mat offPage = new(original.Size(), MatType.CV_8UC1, Scalar.All(255));
                Cv2.FillConvexPoly(offPage, [.. inside.Points().Select(p => new Point((int)Math.Round(p.X), (int)Math.Round(p.Y)))], Scalar.All(0));
                original.SetTo(backgroundBgr, offPage);
            }

            int pad = Math.Max(20, Math.Min(original.Width, original.Height) / 50);
            using Mat padded = new();
            Cv2.CopyMakeBorder(original, padded, pad, pad, pad, pad, BorderTypes.Constant, backgroundBgr);

            if (neutralizeBezels)
            {
                var margins = BackgroundAnalyzer.DetectBezelMargins(original, backgroundHsv, options.BackgroundTolerance);
                BackgroundAnalyzer.PaintBezels(padded, margins, pad, backgroundBgr);
            }

            // The other photos are painted over, so the clicked one can't merge with them.
            foreach (var other in otherBoxes)
            {
                Point[] corners = [.. other.GetVertices().Select(p => new Point((int)Math.Round(p.X) + pad, (int)Math.Round(p.Y) + pad))];
                Cv2.FillConvexPoly(padded, corners, backgroundBgr);
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
        private readonly double _imageArea;
        private readonly List<(RotatedRect Box, double Fill, double Area)> _candidates = [];

        /// <summary>The largest shape around the click left out for covering other photos (an album page, often).</summary>
        public RotatedRect? LargestCovering { get; private set; }

        private readonly double _maxArea;

        public Search(WorkingImage image, Point2f click, IReadOnlyList<RotatedBox> otherBoxes, double maxArea)
        {
            _maxArea = maxArea;
            _image = image;
            _click = click;
            _imageArea = (double)image.ScanSize.Width * image.ScanSize.Height;
            _others = [.. otherBoxes.Select(b => Scale(CvInterop.ToCv(b), image.Scale).Points())];
        }

        /// <summary>The other photos' outlines, in working-size scan coordinates.</summary>
        public Point2f[][] Others => _others;

        /// <summary>
        /// The shapes containing the click in one foreground mask, with how much of its box each fills, in the
        /// order <see cref="Add"/> takes them. Reads nothing that <see cref="Add"/> changes, so masks can be
        /// outlined side by side.
        /// </summary>
        public List<(RotatedRect Box, double Fill)> ShapesAroundClick(Mat foreground)
        {
            var shapes = new List<(RotatedRect Box, double Fill)>();
            // Detection's own candidates for this mask, with composites (several photos as one) resolved.
            var candidates = CandidateExtractor.ExtractCandidates(
                foreground, _image.Pad, _image.ScanSize.Width, _image.ScanSize.Height, MinAreaFraction, MaxAreaFraction, _image.Bgr, _image.BackgroundBgr);
            foreach (var candidate in CandidateResolutionFilter.FilterCandidates(candidates))
            {
                var box = CvInterop.ToCv(candidate.Rotated);
                if (Cv2.PointPolygonTest(box.Points(), _click, false) < 0) continue;
                shapes.Add((box, candidate.Rectangularity));
            }

            // And the mask's raw outlines around the click (the mask is padded: shift by the padding).
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
                shapes.Add((box, boxArea > 0 ? Cv2.ContourArea(contour) / boxArea : 0));
            }
            return shapes;
        }

        public void Add(RotatedRect box, double fill)
        {
            double area = (double)box.Size.Width * box.Size.Height;
            double fraction = area / _imageArea;
            if (fraction < MinAreaFraction || fraction > MaxAreaFraction || area > _maxArea) return;
            if (SwallowsAnother(box, area))
            {
                if (LargestCovering is not { } largest || area > Area(largest)) LargestCovering = box;
                return;
            }
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
