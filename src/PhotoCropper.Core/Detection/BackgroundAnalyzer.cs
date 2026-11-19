using OpenCvSharp;

namespace PhotoCropper.Core.Detection;

public static class BackgroundAnalyzer
{
    public static Scalar SampleBackgroundColor(Mat hsv)
    {
        ArgumentNullException.ThrowIfNull(hsv);
        int s = 15; // sample box size
        if (hsv.Width < s * 3 + 20 || hsv.Height < s * 3 + 20) return new Scalar();

        int halfW = hsv.Width / 2;
        int halfH = hsv.Height / 2;

        // Use adaptive inset (approx 3.5% of dimension, bounded between 5px and 80px)
        // to safely step over flatbed scanner bezels and edge shadow artifacts
        int insetX = Math.Clamp((int)(hsv.Width * 0.035), 5, Math.Max(5, halfW - s - 10));
        int insetY = Math.Clamp((int)(hsv.Height * 0.035), 5, Math.Max(5, halfH - s - 10));
        if (hsv.Width >= 400 && insetX < 25) insetX = 25;
        if (hsv.Height >= 400 && insetY < 25) insetY = 25;

        var rects = new Rect[]
        {
            new(insetX, insetY, s, s),                               // Top-Left
            new(halfW - s / 2, insetY, s, s),                        // Top-Center
            new(hsv.Width - s - insetX, insetY, s, s),               // Top-Right
            new(insetX, halfH - s / 2, s, s),                        // Middle-Left
            new(hsv.Width - s - insetX, halfH - s / 2, s, s),        // Middle-Right
            new(insetX, hsv.Height - s - insetY, s, s),              // Bottom-Left
            new(halfW - s / 2, hsv.Height - s - insetY, s, s),       // Bottom-Center
            new(hsv.Width - s - insetX, hsv.Height - s - insetY, s, s) // Bottom-Right
        };

        var hVals = new List<double>();
        var sVals = new List<double>();
        var vVals = new List<double>();

        foreach (var r in rects)
        {
            using Mat m = new(hsv, r);
            var mean = Cv2.Mean(m);
            hVals.Add(mean.Val0);
            sVals.Add(mean.Val1);
            vVals.Add(mean.Val2);
        }

        hVals.Sort();
        sVals.Sort();
        vVals.Sort();

        // Calculate median for Hue, Saturation, and Value to robustly reject outliers
        double medianH = (hVals[3] + hVals[4]) / 2.0;
        double medianS = (sVals[3] + sVals[4]) / 2.0;
        double medianV = (vVals[3] + vVals[4]) / 2.0;

        return new Scalar(medianH, medianS, medianV);
    }

    /// <summary>
    /// Like <see cref="SampleBackgroundColor(Mat)"/>, for a scan whose photos are partly known: samples around the
    /// border but skips spots inside <paramref name="photos"/> (outlines in <paramref name="hsv"/> pixels) and spots
    /// with texture, so photos covering most of the border don't decide the colour. Falls back to the plain sample
    /// when fewer than two spots are left.
    /// </summary>
    public static Scalar SampleBackgroundColor(Mat hsv, IReadOnlyList<Point2f[]> photos)
    {
        ArgumentNullException.ThrowIfNull(hsv);
        ArgumentNullException.ThrowIfNull(photos);
        const int s = 15;
        if (hsv.Width < s * 3 + 20 || hsv.Height < s * 3 + 20) return SampleBackgroundColor(hsv);

        int insetX = Math.Max(Math.Clamp((int)(hsv.Width * 0.035), 5, hsv.Width / 2 - s - 10), hsv.Width >= 400 ? 25 : 5);
        int insetY = Math.Max(Math.Clamp((int)(hsv.Height * 0.035), 5, hsv.Height / 2 - s - 10), hsv.Height >= 400 ? 25 : 5);
        int x0 = insetX, x1 = hsv.Width - s - insetX, y0 = insetY, y1 = hsv.Height - s - insetY;

        // Five spots along each side (corners shared).
        var spots = new List<Rect>();
        for (int i = 0; i <= 5; i++)
        {
            int x = x0 + (x1 - x0) * i / 5, y = y0 + (y1 - y0) * i / 5;
            spots.Add(new Rect(x, y0, s, s));
            spots.Add(new Rect(x, y1, s, s));
            if (i is > 0 and < 5)
            {
                spots.Add(new Rect(x0, y, s, s));
                spots.Add(new Rect(x1, y, s, s));
            }
        }

        var hVals = new List<double>();
        var sVals = new List<double>();
        var vVals = new List<double>();
        foreach (var r in spots)
        {
            var centre = new Point2f(r.X + s / 2f, r.Y + s / 2f);
            if (photos.Any(p => Cv2.PointPolygonTest(p, centre, true) > -s)) continue; // on (or right next to) a photo
            using Mat m = new(hsv, r);
            Cv2.MeanStdDev(m, out Scalar mean, out Scalar spread);
            if (spread.Val2 > 12) continue; // textured: not plain background
            hVals.Add(mean.Val0);
            sVals.Add(mean.Val1);
            vVals.Add(mean.Val2);
        }
        if (hVals.Count < 2) return SampleBackgroundColor(hsv);
        return new Scalar(Median(hVals), Median(sVals), Median(vVals));
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        int n = values.Count;
        return n % 2 == 1 ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) / 2;
    }

    public static Scalar SamplePixelBackgroundColor(Mat bgr, int x, int y, int sampleSize = 5)
    {
        ArgumentNullException.ThrowIfNull(bgr);
        if (bgr.Empty()) return new Scalar();

        x = Math.Clamp(x, 0, bgr.Width - 1);
        y = Math.Clamp(y, 0, bgr.Height - 1);

        int half = sampleSize / 2;
        int startX = Math.Max(0, x - half);
        int startY = Math.Max(0, y - half);
        int w = Math.Min(bgr.Width - startX, sampleSize);
        int h = Math.Min(bgr.Height - startY, sampleSize);

        using Mat sampledArea = new(bgr, new Rect(startX, startY, w, h));
        using Mat bgrSampled = PhotoCropperEngine.NormalizeToBgr(sampledArea);
        using Mat hsvSampled = new();
        Cv2.CvtColor(bgrSampled, hsvSampled, ColorConversionCodes.BGR2HSV);
        return Cv2.Mean(hsvSampled);
    }

    public static Scalar HsvToBgr(Scalar hsvColor)
    {
        using Mat hsvPixel = new(1, 1, MatType.CV_8UC3);
        hsvPixel.SetTo(hsvColor);
        using Mat bgrPixel = new();
        Cv2.CvtColor(hsvPixel, bgrPixel, ColorConversionCodes.HSV2BGR);
        return Cv2.Mean(bgrPixel);
    }

    public static Mat CreateBackgroundMask(Mat hsv, Scalar avgColor, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(hsv);

        // If the background is neutral / light (low saturation, high value like scanner lids),
        // hue is unstable and noise-prone. In this regime, match based on low saturation
        // and allow an extended Value range to absorb CIS sensor lid shadows and off-white gradients.
        bool isNeutralLight = avgColor.Val1 < 65 && avgColor.Val2 > 110;

        Scalar lower;
        Scalar upper;

        if (isNeutralLight)
        {
            // Background tolerance scales realistically to physical scanner lid variance:
            // Scanner lids vary by ~10-25 levels due to CIS lighting falloff and texture.
            // Tolerance 0: tight match to lid (V within 8 levels)
            // Tolerance 25 (default): absorbs ~19 levels of shadow
            // Tolerance 100: absorbs up to ~53 levels of shadow without eating photo content (V stays >= 160)
            double maxS = Math.Clamp(avgColor.Val1 + 10.0 + (tolerance * 0.25), 25.0, 60.0);
            double minV = Math.Max(160.0, avgColor.Val2 - (8.0 + tolerance * 0.45));

            lower = new Scalar(0, 0, minV);
            upper = new Scalar(180, maxS, 255);
        }
        else
        {
            double hTol = tolerance * 0.4;
            double sTol = tolerance;
            double vTol = avgColor.Val2 > 128 ? tolerance * 1.5 : tolerance;

            lower = new Scalar(Math.Max(0, avgColor.Val0 - hTol), Math.Max(0, avgColor.Val1 - sTol), Math.Max(0, avgColor.Val2 - vTol));
            upper = new Scalar(Math.Min(180, avgColor.Val0 + hTol), Math.Min(255, avgColor.Val1 + sTol), Math.Min(255, avgColor.Val2 + vTol));
        }

        Mat mask = new();
        Scalar lowerArray = lower;
        Scalar upperArray = upper;
        Cv2.InRange(hsv, lowerArray, upperArray, mask);
        return mask;
    }

    public static (int Top, int Bottom, int Left, int Right) DetectBezelMargins(Mat source, Scalar avgBgHsv, double tolerance = 30)
    {
        ArgumentNullException.ThrowIfNull(source);

        int w = source.Width;
        int h = source.Height;
        if (w < 40 || h < 40) return (0, 0, 0, 0);

        // Downscale for fast bezel detection if source is large
        double scale = Math.Min(1.0, 800.0 / Math.Max(w, h));
        int scaledW = (int)Math.Round(w * scale);
        int scaledH = (int)Math.Round(h * scale);

        using Mat rawSmallSource = new();
        if (scale < 0.999)
        {
            Cv2.Resize(source, rawSmallSource, new Size(scaledW, scaledH), 0, 0, InterpolationFlags.Area);
        }
        else
        {
            source.CopyTo(rawSmallSource);
        }

        using Mat smallSource = PhotoCropperEngine.NormalizeToBgr(rawSmallSource);

        using Mat hsv = new();
        Cv2.CvtColor(smallSource, hsv, ColorConversionCodes.BGR2HSV);
        using Mat bgMask = CreateBackgroundMask(hsv, avgBgHsv, tolerance);

        int maxBezelX = Math.Clamp(scaledW / 30, 4, 30);
        int maxBezelY = Math.Clamp(scaledH / 30, 4, 30);

        int topBezel = MeasureBezelDepth(bgMask, maxBezelY, scaledW, true, true);
        int bottomBezel = MeasureBezelDepth(bgMask, maxBezelY, scaledW, true, false);
        int leftBezel = MeasureBezelDepth(bgMask, maxBezelX, scaledH, false, true);
        int rightBezel = MeasureBezelDepth(bgMask, maxBezelX, scaledH, false, false);

        double invScale = 1.0 / scale;
        return (
            (int)Math.Round(topBezel * invScale),
            (int)Math.Round(bottomBezel * invScale),
            (int)Math.Round(leftBezel * invScale),
            (int)Math.Round(rightBezel * invScale)
        );
    }

    private static int MeasureBezelDepth(Mat bgMask, int maxDepth, int length, bool isHorizontal, bool fromStart)
    {
        int bezel = 0;
        int crossLength = isHorizontal ? bgMask.Height : bgMask.Width;
        maxDepth = Math.Min(maxDepth, crossLength);

        for (int i = 0; i < maxDepth; i++)
        {
            int idx = fromStart ? i : crossLength - 1 - i;
            var rect = isHorizontal ? new Rect(0, idx, length, 1) : new Rect(idx, 0, 1, length);

            using Mat slice = new(bgMask, rect);
            int nonBg = length - Cv2.CountNonZero(slice);

            if (nonBg >= length * 0.70)
            {
                bezel = i + 1;
            }
            else if (nonBg < length * 0.40)
            {
                break;
            }
        }

        return bezel >= maxDepth ? 0 : bezel;
    }
}
