using System.Runtime.InteropServices;
using OpenCvSharp;

namespace PhotoCropper.Core.Detection;

/// <summary>
/// Finds the photos on a light album page by texture and straight seams, for pages where colour can't tell them from
/// the paper (faded prints on cream or salmon paper, prints stuck edge to edge). Paper is smooth and of one colour;
/// a photo has detail. So the page is cut into small tiles, and a tile is paper only when it is both smooth and the
/// paper's colour: the rest is photo, with smooth parts inside a photo (a wall, a face) filled in. Photos stuck edge
/// to edge come out as one area; it is divided along seams, the rows and columns of the (straightened) page where a
/// straight edge runs right across it, into pieces that each have a print's proportions.
/// </summary>
/// <remarks>
/// Measured on the 4 real light pages that detection took whole: 8 of their 15 photos found (none before). The
/// pieces follow the tile grid, so their edges can be off by up to a tile; Auto-Tune and Find Photo refine them.
/// </remarks>
internal static class TextureSeamSplitter
{
    /// <summary>The page is worked at this long side.</summary>
    private const int WorkSide = 1000;

    /// <summary>Tile size, in working pixels.</summary>
    private const int Tile = 12;

    /// <summary>A tile is smooth when its brightness varies less than this (standard deviation, 0..255).</summary>
    private const double SmoothStdDev = 7;

    /// <summary>A print's proportions: 3:2, 4:3, 5:7, square... at most about 16:9.</summary>
    private const double MaxPrintAspect = 1.8;

    /// <summary>A seam has edge support along at least this share of its length across the area.</summary>
    private const double MinSeamSupport = 0.75;

    /// <summary>
    /// ...and more when the area already has a print's proportions: it may be one print, and a straight line inside
    /// a picture (a table's edge) is then not a seam. Real seams on the real pages: 83-99 %; lines inside photos: 76-81 %.
    /// </summary>
    private const double MinSeamSupportInPrint = 0.85;

    /// <summary>Seams are looked for away from an area's ends (a seam near an end would leave a sliver).</summary>
    private const double SeamMargin = 0.12;

    /// <summary>How many seams are tried at each level, and how deep the division goes.</summary>
    private const int SeamsTried = 4, MaxDepth = 4;

    /// <summary>
    /// Rough boxes of the photos on <paramref name="page"/> (an album page straightened into its own image, BGR),
    /// in its pixels. Empty when the page can't be divided into print-shaped pieces.
    /// </summary>
    /// <param name="paperHsv">The page's paper colour (OpenCV HSV).</param>
    /// <param name="minArea">The smallest photo, in <paramref name="page"/>'s pixels.</param>
    public static List<RotatedRect> FindPieces(Mat page, Scalar paperHsv, double minArea)
    {
        ArgumentNullException.ThrowIfNull(page);
        double k = Math.Min(1.0, (double)WorkSide / Math.Max(page.Width, page.Height));
        using Mat small = new();
        Cv2.Resize(page, small, new Size(Math.Max(1, (int)Math.Round(page.Width * k)), Math.Max(1, (int)Math.Round(page.Height * k))), 0, 0, InterpolationFlags.Area);
        double smallMinArea = minArea * k * k;

        using Mat photo = PhotoTiles(small, paperHsv);
        using Mat gray = new();
        Cv2.CvtColor(small, gray, ColorConversionCodes.BGR2GRAY);
        using Mat edges = new();
        using (Mat blurred = new())
        {
            Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);
            Cv2.Canny(blurred, edges, 25, 75);
        }
        using Mat support = new();
        using (Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5))) Cv2.Dilate(edges, support, kernel);

        var pieces = new List<RotatedRect>();
        Cv2.FindContours(photo, out Point[][] areas, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        foreach (var area in areas.Where(a => Cv2.ContourArea(a) >= smallMinArea))
        {
            using Mat blob = new(photo.Size(), MatType.CV_8UC1, Scalar.All(0));
            Cv2.DrawContours(blob, [area], 0, Scalar.All(255), -1);
            // Seams are judged on the area's own edges only, not on its outline.
            using Mat inside = new();
            using (Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5))) Cv2.Erode(blob, inside, kernel);
            using Mat blobSupport = new();
            Cv2.BitwiseAnd(support, inside, blobSupport);
            if (Divide(blob, blobSupport, smallMinArea, 0) is { } found) pieces.AddRange(found);
        }

        return [.. pieces.Select(p => new RotatedRect(
            new Point2f((float)(p.Center.X / k), (float)(p.Center.Y / k)),
            new Size2f((float)(p.Size.Width / k), (float)(p.Size.Height / k)),
            p.Angle))];
    }

    /// <summary>
    /// 255 where the page is photo: every tile that isn't paper (smooth and the paper's colour), cleaned of specks,
    /// with each area's holes filled. A thin band along the page's edges is left out.
    /// </summary>
    private static Mat PhotoTiles(Mat small, Scalar paper)
    {
        using Mat hsv = new();
        Cv2.CvtColor(small, hsv, ColorConversionCodes.BGR2HSV);
        int width = small.Width, height = small.Height, band = 9;
        var photo = new Mat(height, width, MatType.CV_8UC1, Scalar.All(0));
        try
        {
            for (int y = band; y + Tile <= height - band; y += Tile)
            {
                for (int x = band; x + Tile <= width - band; x += Tile)
                {
                    using Mat tile = new(hsv, new Rect(x, y, Tile, Tile));
                    Cv2.MeanStdDev(tile, out Scalar mean, out Scalar spread);
                    bool isPaper = spread.Val2 <= SmoothStdDev
                        && Math.Abs(mean.Val0 - paper.Val0) <= 10
                        && Math.Abs(mean.Val1 - paper.Val1) <= 28
                        && Math.Abs(mean.Val2 - paper.Val2) <= 28;
                    if (!isPaper) Cv2.Rectangle(photo, new Rect(x, y, Tile, Tile), Scalar.All(255), -1);
                }
            }

            using (Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size((2 * Tile) + 1, (2 * Tile) + 1)))
            {
                Cv2.MorphologyEx(photo, photo, MorphTypes.Open, kernel);
            }
            Cv2.FindContours(photo, out Point[][] areas, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            for (int i = 0; i < areas.Length; i++) Cv2.DrawContours(photo, areas, i, Scalar.All(255), -1);
            return photo;
        }
        catch
        {
            photo.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The pieces <paramref name="area"/> divides into along seams, each with a print's proportions; null when it
    /// can't be. An area that is already print-shaped needs a stronger seam to be divided.
    /// </summary>
    private static List<RotatedRect>? Divide(Mat area, Mat support, double minArea, int depth)
    {
        bool isPrint = IsPrint(area, minArea, out RotatedRect whole);
        if (depth <= MaxDepth)
        {
            foreach (var (strength, horizontal, at) in Seams(area, support))
            {
                if (isPrint && strength < MinSeamSupportInPrint) continue;
                using Mat first = area.Clone();
                using Mat second = area.Clone();
                // Cut along the seam: everything from it on is taken off one piece, everything up to it off the other.
                int rows = area.Rows, cols = area.Cols;
                if (horizontal)
                {
                    Cv2.Rectangle(first, new Rect(0, Math.Max(0, at - 1), cols, rows), Scalar.All(0), -1);
                    Cv2.Rectangle(second, new Rect(0, 0, cols, Math.Min(rows, at + 2)), Scalar.All(0), -1);
                }
                else
                {
                    Cv2.Rectangle(first, new Rect(Math.Max(0, at - 1), 0, cols, rows), Scalar.All(0), -1);
                    Cv2.Rectangle(second, new Rect(0, 0, Math.Min(cols, at + 2), rows), Scalar.All(0), -1);
                }
                if (Cv2.CountNonZero(first) < 0.8 * minArea || Cv2.CountNonZero(second) < 0.8 * minArea) continue;
                if (Divide(first, support, minArea, depth + 1) is not { } a) continue;
                if (Divide(second, support, minArea, depth + 1) is not { } b) continue;
                return [.. a, .. b];
            }
        }
        return isPrint ? [whole] : null;
    }

    /// <summary>Whether the area has a print's size and proportions; its box either way.</summary>
    private static bool IsPrint(Mat area, double minArea, out RotatedRect box)
    {
        using Mat points = new();
        Cv2.FindNonZero(area, points);
        box = points.Empty() ? default : Cv2.MinAreaRect(points);
        float w = box.Size.Width, h = box.Size.Height;
        return w * h >= minArea && Math.Max(w, h) / Math.Max(1f, Math.Min(w, h)) <= MaxPrintAspect;
    }

    /// <summary>
    /// The rows and columns across the area with the most edge support (the share of the line, inside the area, that
    /// lies on an edge), strongest first. Neighbouring rows of one seam count once.
    /// </summary>
    private static List<(double Strength, bool Horizontal, int At)> Seams(Mat area, Mat support)
    {
        var bounds = Cv2.BoundingRect(area);
        var found = new List<(double, bool, int)>();
        foreach (bool horizontal in new[] { true, false })
        {
            float[] inside = Sums(area, horizontal), onEdge = Sums(support, horizontal);
            int from = horizontal ? bounds.Top : bounds.Left, to = horizontal ? bounds.Bottom : bounds.Right;
            int span = horizontal ? bounds.Width : bounds.Height, margin = (int)(SeamMargin * (to - from));
            for (int i = from + margin; i < to - margin; i++)
            {
                if (inside[i] < 0.5 * span) continue; // the line must cross most of the area
                double strength = onEdge[i] / inside[i];
                if (strength >= MinSeamSupport) found.Add((strength, horizontal, i));
            }
        }

        var picked = new List<(double Strength, bool Horizontal, int At)>();
        foreach (var seam in found.OrderByDescending(s => s.Item1))
        {
            if (picked.All(p => p.Horizontal != seam.Item2 || Math.Abs(p.At - seam.Item3) >= 15)) picked.Add(seam);
            if (picked.Count == SeamsTried) break;
        }
        return picked;
    }

    /// <summary>The number of set pixels in each row (or column) of a 0/255 mask.</summary>
    private static float[] Sums(Mat mask, bool rows)
    {
        using Mat ones = new();
        mask.ConvertTo(ones, MatType.CV_32F, 1 / 255.0);
        using Mat sums = new();
        Cv2.Reduce(ones, sums, rows ? ReduceDimension.Column : ReduceDimension.Row, ReduceTypes.Sum, MatType.CV_32F);
        var values = new float[sums.Rows * sums.Cols];
        Marshal.Copy(sums.Data, values, 0, values.Length);
        return values;
    }
}
