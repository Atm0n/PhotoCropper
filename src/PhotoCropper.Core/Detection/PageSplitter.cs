using OpenCvSharp;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Models;

namespace PhotoCropper.Core.Detection;

/// <summary>
/// Finds the photos on an album page that detection took as one box. On a light page (cream, pink) faded photos are
/// close to the paper's colour, and the page stands out from the scanner bed far more than they stand out from it:
/// the page's outline wins. Find Photo already searches such a page again against its paper
/// (<see cref="PhotoAtPointLocator"/>); this does the same for automatic detection.
/// </summary>
internal static class PageSplitter
{
    /// <summary>A box smaller than this share of the scan is a photo, not a page.</summary>
    private const double MinPageShare = 0.25;

    /// <summary>A box found inside the page larger than this share of it is the page again, not a photo.</summary>
    private const double MaxPhotoShareOfPage = 0.6;

    /// <summary>How many photos must be found inside for the page box to be replaced by them.</summary>
    private const int MinPhotosInside = 2;

    /// <summary>
    /// <paramref name="candidates"/>, with each page among them replaced by the photos found on it.
    /// A box stays as it is unless it has plain paper just inside its edges and at least two photos inside.
    /// </summary>
    public static IReadOnlyList<CropCandidate> Split(Mat original, IReadOnlyList<CropCandidate> candidates, DetectionOptions options)
    {
        double scanArea = (double)original.Width * original.Height;
        var result = new List<CropCandidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var inside = candidate.Area >= MinPageShare * scanArea ? PhotosInside(original, candidate, options) : null;
            if (inside is { Count: >= MinPhotosInside }) result.AddRange(inside);
            else result.Add(candidate);
        }
        return result;
    }

    private static List<CropCandidate>? PhotosInside(Mat original, CropCandidate page, DetectionOptions options)
    {
        var box = CvInterop.ToCv(page.Rotated);
        int w = (int)Math.Round(box.Size.Width), h = (int)Math.Round(box.Size.Height);
        if (w < 100 || h < 100) return null;

        // The page straightened into its own image: the box is [0, w] x [0, h].
        using Mat toPage = Cv2.GetRotationMatrix2D(box.Center, box.Angle, 1.0);
        toPage.Set<double>(0, 2, toPage.Get<double>(0, 2) - box.Center.X + w / 2.0);
        toPage.Set<double>(1, 2, toPage.Get<double>(1, 2) - box.Center.Y + h / 2.0);
        using Mat patch = new();
        Cv2.WarpAffine(original, patch, toPage, new Size(w, h), InterpolationFlags.Linear, BorderTypes.Replicate);

        if (PaperColour(patch) is not { } paper) return null;

        // Detected again with the paper as the background. The minimum size stays a share of the whole scan.
        var onPaper = options with { CustomBackgroundColorHsv = CvInterop.ToColor(paper) };
        using var prepared = PhotoDetector.Prepare(patch, onPaper);
        var found = PhotoDetector.FindCandidates(patch, prepared, onPaper, lookInsidePages: false);

        double minArea = options.MinAreaFactor * original.Width * original.Height;
        double maxArea = MaxPhotoShareOfPage * w * h;
        var photos = found.Where(c => c.Area >= minArea && c.Area <= maxArea).ToList();
        if (photos.Count < MinPhotosInside) return null;

        using Mat toScan = new();
        Cv2.InvertAffineTransform(toPage, toScan);
        return [.. photos.Select(c => ToScan(c, toScan, original))];
    }

    /// <summary>
    /// The colour of the paper when the box looks like an album page: its most common smooth colour covers a good
    /// part of it, and is light without being the white scanner bed. Looked for all over the box, not only along its
    /// edges: old pages have stains, and photos often reach their edges. On 22 real light pages the paper covers 19-46%
    /// of the scan (10% on one), saturation 35-64 and brightness 191-237.
    /// </summary>
    private static Scalar? PaperColour(Mat patch)
    {
        using Mat hsv = new();
        Cv2.CvtColor(patch, hsv, ColorConversionCodes.BGR2HSV);
        int width = hsv.Width, height = hsv.Height;
        int tile = Math.Max(16, Math.Min(width, height) / 70);

        var smooth = new List<Scalar>();
        int total = 0;
        for (int y = 0; y + tile <= height; y += tile)
        {
            for (int x = 0; x + tile <= width; x += tile)
            {
                total++;
                using Mat m = new(hsv, new Rect(x, y, tile, tile));
                Cv2.MeanStdDev(m, out Scalar mean, out Scalar spread);
                // Smooth, and not the scanner bed (white or light grey: hardly any saturation).
                if (spread.Val2 <= 8 && mean.Val1 > 15) smooth.Add(mean);
            }
        }
        if (total == 0 || smooth.Count < 0.08 * total) return null;

        static double Median(IEnumerable<double> values)
        {
            var sorted = values.Order().ToList();
            return sorted[sorted.Count / 2];
        }
        var paper = new Scalar(Median(smooth.Select(c => c.Val0)), Median(smooth.Select(c => c.Val1)), Median(smooth.Select(c => c.Val2)));

        // That one paper covers a good part of the box (photos have plain areas too, so not most of what is smooth),
        // and it is light and soft-coloured (cream, pink, beige): not a sky or a wall inside a single photo, which
        // detection should leave whole. On the real light pages it covers 12-32% of the box.
        int agreeing = smooth.Count(c => Math.Abs(c.Val1 - paper.Val1) <= 15 && Math.Abs(c.Val2 - paper.Val2) <= 15);
        bool onePaper = agreeing >= 0.10 * total;
        bool pageLike = paper.Val2 >= 170 && paper.Val1 <= 90;
        return onePaper && pageLike ? paper : null;
    }

    /// <summary>A box found on the straightened page, mapped back to the scan.</summary>
    private static CropCandidate ToScan(CropCandidate c, Mat toScan, Mat original)
    {
        Point2f Map(Point2f p) => new(
            (float)(toScan.Get<double>(0, 0) * p.X + toScan.Get<double>(0, 1) * p.Y + toScan.Get<double>(0, 2)),
            (float)(toScan.Get<double>(1, 0) * p.X + toScan.Get<double>(1, 1) * p.Y + toScan.Get<double>(1, 2)));

        var corners = CvInterop.ToCv(c.Rotated).Points().Select(Map).ToArray();
        var rotated = Cv2.MinAreaRect(corners);
        var shape = c.ShapePoints.Length > 0
            ? c.ShapePoints.Select(p => Map(new Point2f(p.X, p.Y))).Select(p => new Point((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToArray()
            : corners.Select(p => new Point((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToArray();
        var clamped = shape.Select(p => new Point(Math.Clamp(p.X, 0, original.Width - 1), Math.Clamp(p.Y, 0, original.Height - 1))).ToArray();
        return new CropCandidate(
            CvInterop.ToPoints(clamped),
            CvInterop.ToRectangle(Cv2.BoundingRect(clamped)),
            c.Score,
            CvInterop.ToBox(rotated),
            c.Area,
            c.Rectangularity,
            c.Convexity);
    }
}
