using OpenCvSharp;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Models;

namespace PhotoCropper.Core.Detection;

/// <summary>
/// Moves each side of a roughly placed box onto the photo's physical edge (the print's edge, so a white border stays
/// in). Each side takes the strong edge nearest to where it is, in several segments, and a line is fitted
/// through the segments' edges, so a box that is a little too big, too small or tilted ends up on the photo.
/// </summary>
public static class BoxEdgeFitter
{
    /// <summary>How far past each side the edge is searched, as a fraction of the box's shorter side.</summary>
    internal static double SearchFraction = Env("PC_FIT_SEARCH", 0.05);

    /// <summary>An edge counts once its strength reaches this fraction of the strongest edge found on that segment.</summary>
    internal static double EdgeFraction = Env("PC_FIT_EDGE", 0.45);

    /// <summary>How quickly an edge further from the side counts less (fraction of the search band).</summary>
    internal static double ProximityScale = Env("PC_FIT_NEAR", 0.35);

    private const int SegmentsPerSide = 8;

    private static double Env(string name, double fallback) =>
        double.TryParse(Environment.GetEnvironmentVariable(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : fallback;

    /// <summary>Returns <paramref name="box"/> fitted to the photo's edges, or the box unchanged when no edges are clear.</summary>
    public static RotatedBox Fit(Mat scan, RotatedBox box)
    {
        ArgumentNullException.ThrowIfNull(scan);
        if (scan.Empty() || box.Size.Width < 20 || box.Size.Height < 20) return box;

        // Straighten the box and a margin around it: in the patch the box is axis-aligned, centred, margin m.
        double shorter = Math.Min(box.Size.Width, box.Size.Height);
        int m = Math.Max(6, (int)Math.Round(shorter * SearchFraction));
        int w = (int)Math.Round(box.Size.Width), h = (int)Math.Round(box.Size.Height);
        using Mat patch = Straighten(scan, box, w + 2 * m, h + 2 * m);
        using Mat gray = new();
        Cv2.CvtColor(patch, gray, patch.Channels() == 4 ? ColorConversionCodes.BGRA2GRAY : ColorConversionCodes.BGR2GRAY);
        Cv2.GaussianBlur(gray, gray, new Size(5, 5), 0);
        using Mat gx = Gradient(gray, 1, 0);
        using Mat gy = Gradient(gray, 0, 1);

        // Each side as a line in patch coordinates: left/right are x = a*y + b, top/bottom are y = a*x + b.
        var left = FitSide(gx, vertical: true, fromStart: true, m, w, h);
        var right = FitSide(gx, vertical: true, fromStart: false, m, w, h);
        var top = FitSide(gy, vertical: false, fromStart: true, m, w, h);
        var bottom = FitSide(gy, vertical: false, fromStart: false, m, w, h);
        if (left is null || right is null || top is null || bottom is null) return box;

        Point2f[] corners = [Cross(left.Value, top.Value), Cross(right.Value, top.Value), Cross(right.Value, bottom.Value), Cross(left.Value, bottom.Value)];
        RotatedRect fitted = Cv2.MinAreaRect(corners);

        // Back to scan coordinates: undo the straightening (rotation by the box's angle about its centre).
        double rad = box.Angle * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
        double dx = fitted.Center.X - (w + 2 * m) / 2.0, dy = fitted.Center.Y - (h + 2 * m) / 2.0;
        var center = new Point2f((float)(box.Center.X + dx * cos - dy * sin), (float)(box.Center.Y + dx * sin + dy * cos));
        var result = new RotatedRect(center, fitted.Size, fitted.Angle + box.Angle);

        // A fit that changed the box a lot found the wrong edges (or none): keep what the user had.
        double before = (double)box.Size.Width * box.Size.Height, after = (double)result.Size.Width * result.Size.Height;
        if (after < 0.6 * before || after > 1.4 * before) return box;
        return CvInterop.ToBox(Extraction.PhotoExtractionEngine.RegularizeNearRightAngles(result));
    }

    /// <summary>The absolute Sobel gradient in one direction, as 8-bit.</summary>
    private static Mat Gradient(Mat gray, int dx, int dy)
    {
        using Mat signed = new();
        Cv2.Sobel(gray, signed, MatType.CV_32F, dx, dy, 3);
        Mat magnitude = new();
        Cv2.ConvertScaleAbs(signed, magnitude, 0.25);
        return magnitude;
    }

    /// <summary>The box (and the margin around it) rotated upright into a <paramref name="width"/> x <paramref name="height"/> patch.</summary>
    private static Mat Straighten(Mat scan, RotatedBox box, int width, int height)
    {
        using Mat rotation = Cv2.GetRotationMatrix2D(new Point2f(box.Center.X, box.Center.Y), box.Angle, 1.0);
        // Shift so the box's centre lands in the middle of the patch.
        rotation.Set<double>(0, 2, rotation.Get<double>(0, 2) - box.Center.X + width / 2.0);
        rotation.Set<double>(1, 2, rotation.Get<double>(1, 2) - box.Center.Y + height / 2.0);
        Mat patch = new();
        Cv2.WarpAffine(scan, patch, rotation, new Size(width, height), InterpolationFlags.Linear, BorderTypes.Replicate);
        return patch;
    }

    /// <summary>
    /// Finds one side's edge in each segment, searching the band [0, 2m] across the side from outside inwards, and
    /// fits a line through them. Null when too few segments show an edge.
    /// </summary>
    private static (double A, double B)? FitSide(Mat gradient, bool vertical, bool fromStart, int m, int w, int h)
    {
        int along = vertical ? h : w;               // the side's length
        int sideAt = fromStart ? m : (vertical ? w : h) + m; // the side's position in the patch
        var samples = new List<(double T, double Offset)>();
        for (int s = 0; s < SegmentsPerSide; s++)
        {
            // Skip the corners, where the other sides' edges are.
            int t0 = m + (int)(along * (0.1 + 0.8 * s / SegmentsPerSide));
            int t1 = m + (int)(along * (0.1 + 0.8 * (s + 1) / SegmentsPerSide));
            if (t1 <= t0) continue;

            // Edge strength per offset across the side, averaged along the segment.
            var profile = new double[2 * m + 1];
            for (int o = 0; o <= 2 * m; o++)
            {
                int across = fromStart ? sideAt - m + o : sideAt + m - o; // o = 0 is outside, 2m is inside
                Rect line = vertical ? new Rect(across, t0, 1, t1 - t0) : new Rect(t0, across, t1 - t0, 1);
                line = line.Intersect(new Rect(0, 0, gradient.Width, gradient.Height));
                if (line.Width <= 0 || line.Height <= 0) continue;
                using Mat cells = new(gradient, line);
                profile[o] = Cv2.Mean(cells).Val0;
            }

            double max = profile.Max();
            if (max < 8) continue; // flat: no edge here
            // The strong edge nearest the side as placed: a loose side moves in onto the photo, an accurate one stays,
            // and a neighbouring photo's edge further out doesn't pull it away.
            int bestOffset = -1;
            double bestScore = 0;
            for (int o = 1; o < 2 * m; o++)
            {
                if (profile[o] < EdgeFraction * max || profile[o] < profile[o - 1] || profile[o] < profile[o + 1]) continue;
                double distance = Math.Abs(o - m) / (double)m;
                double score = profile[o] / max * Math.Exp(-distance / ProximityScale);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestOffset = o;
                }
            }
            if (bestOffset >= 0)
            {
                double across = fromStart ? sideAt - m + bestOffset : sideAt + m - bestOffset;
                samples.Add(((t0 + t1) / 2.0, across));
            }
        }
        if (samples.Count < SegmentsPerSide / 2) return null;
        return RobustLine(samples);
    }

    /// <summary>A line offset = a*t + b through the samples, ignoring outliers (median of pairwise slopes).</summary>
    private static (double A, double B) RobustLine(List<(double T, double Offset)> samples)
    {
        var slopes = new List<double>();
        for (int i = 0; i < samples.Count; i++)
        {
            for (int j = i + 1; j < samples.Count; j++)
            {
                double dt = samples[j].T - samples[i].T;
                if (Math.Abs(dt) > 1e-6) slopes.Add((samples[j].Offset - samples[i].Offset) / dt);
            }
        }
        double a = slopes.Count > 0 ? Median(slopes) : 0;
        double b = Median([.. samples.Select(s => s.Offset - a * s.T)]);
        return (a, b);
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        int n = values.Count;
        return n % 2 == 1 ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) / 2;
    }

    /// <summary>Where a vertical side (x = a*y + b) meets a horizontal side (y = c*x + d).</summary>
    private static Point2f Cross((double A, double B) vertical, (double A, double B) horizontal)
    {
        // x = a*y + b, y = c*x + d  =>  x = a*(c*x + d) + b  =>  x = (a*d + b) / (1 - a*c)
        double a = vertical.A, b = vertical.B, c = horizontal.A, d = horizontal.B;
        double x = (a * d + b) / (1 - a * c);
        return new Point2f((float)x, (float)(c * x + d));
    }
}
