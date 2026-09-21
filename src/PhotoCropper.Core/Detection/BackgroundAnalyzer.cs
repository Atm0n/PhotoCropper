using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System.Drawing;

namespace PhotoCropper.Core.Detection;

public static class BackgroundAnalyzer
{
    public static MCvScalar SampleBackgroundColor(Mat hsv)
    {
        ArgumentNullException.ThrowIfNull(hsv);
        int s = 15; // sample box size
        if (hsv.Width < s * 3 + 20 || hsv.Height < s * 3 + 20) return new MCvScalar();

        int halfW = hsv.Width / 2;
        int halfH = hsv.Height / 2;

        // Use adaptive inset (approx 3.5% of dimension, bounded between 5px and 80px)
        // to safely step over flatbed scanner bezels and edge shadow artifacts
        int insetX = Math.Clamp((int)(hsv.Width * 0.035), 5, Math.Max(5, halfW - s - 10));
        int insetY = Math.Clamp((int)(hsv.Height * 0.035), 5, Math.Max(5, halfH - s - 10));
        if (hsv.Width >= 400 && insetX < 25) insetX = 25;
        if (hsv.Height >= 400 && insetY < 25) insetY = 25;

        var rects = new Rectangle[]
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
            var mean = CvInvoke.Mean(m);
            hVals.Add(mean.V0);
            sVals.Add(mean.V1);
            vVals.Add(mean.V2);
        }

        hVals.Sort();
        sVals.Sort();
        vVals.Sort();

        // Calculate median for Hue, Saturation, and Value to robustly reject outliers
        double medianH = (hVals[3] + hVals[4]) / 2.0;
        double medianS = (sVals[3] + sVals[4]) / 2.0;
        double medianV = (vVals[3] + vVals[4]) / 2.0;

        return new MCvScalar(medianH, medianS, medianV);
    }

    public static MCvScalar SamplePixelBackgroundColor(Mat bgr, int x, int y, int sampleSize = 5)
    {
        ArgumentNullException.ThrowIfNull(bgr);
        if (bgr.IsEmpty) return new MCvScalar();

        x = Math.Clamp(x, 0, bgr.Width - 1);
        y = Math.Clamp(y, 0, bgr.Height - 1);

        int half = sampleSize / 2;
        int startX = Math.Max(0, x - half);
        int startY = Math.Max(0, y - half);
        int w = Math.Min(bgr.Width - startX, sampleSize);
        int h = Math.Min(bgr.Height - startY, sampleSize);

        using Mat sampledArea = new(bgr, new Rectangle(startX, startY, w, h));
        using Mat bgrSampled = PhotoCropperEngine.NormalizeToBgr(sampledArea);
        using Mat hsvSampled = new();
        CvInvoke.CvtColor(bgrSampled, hsvSampled, ColorConversion.Bgr2Hsv);
        return CvInvoke.Mean(hsvSampled);
    }

    public static MCvScalar HsvToBgr(MCvScalar hsvColor)
    {
        using Mat hsvPixel = new(1, 1, DepthType.Cv8U, 3);
        hsvPixel.SetTo(hsvColor);
        using Mat bgrPixel = new();
        CvInvoke.CvtColor(hsvPixel, bgrPixel, ColorConversion.Hsv2Bgr);
        return CvInvoke.Mean(bgrPixel);
    }

    public static Mat CreateBackgroundMask(Mat hsv, MCvScalar avgColor, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(hsv);

        // If the background is neutral / light (low saturation, high value like scanner lids),
        // hue is unstable and noise-prone. In this regime, match based on low saturation
        // and allow an extended Value range to absorb CIS sensor lid shadows and off-white gradients.
        bool isNeutralLight = avgColor.V1 < 65 && avgColor.V2 > 110;

        MCvScalar lower;
        MCvScalar upper;

        if (isNeutralLight)
        {
            double maxS = Math.Min(255, Math.Max(avgColor.V1 + tolerance * 1.5, 65));
            double minV = Math.Max(40, avgColor.V2 - tolerance * 3.5);

            lower = new MCvScalar(0, 0, minV);
            upper = new MCvScalar(180, maxS, 255);
        }
        else
        {
            double hTol = tolerance * 0.4;
            double sTol = tolerance;
            double vTol = avgColor.V2 > 128 ? tolerance * 1.5 : tolerance;

            lower = new MCvScalar(Math.Max(0, avgColor.V0 - hTol), Math.Max(0, avgColor.V1 - sTol), Math.Max(0, avgColor.V2 - vTol));
            upper = new MCvScalar(Math.Min(180, avgColor.V0 + hTol), Math.Min(255, avgColor.V1 + sTol), Math.Min(255, avgColor.V2 + vTol));
        }

        Mat mask = new();
        using ScalarArray lowerArray = new(lower);
        using ScalarArray upperArray = new(upper);
        CvInvoke.InRange(hsv, lowerArray, upperArray, mask);
        return mask;
    }

    public static (int Top, int Bottom, int Left, int Right) DetectBezelMargins(Mat source, MCvScalar avgBgHsv, double tolerance = 30)
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
            CvInvoke.Resize(source, rawSmallSource, new Size(scaledW, scaledH), 0, 0, Inter.Area);
        }
        else
        {
            source.CopyTo(rawSmallSource);
        }

        using Mat smallSource = PhotoCropperEngine.NormalizeToBgr(rawSmallSource);

        using Mat hsv = new();
        CvInvoke.CvtColor(smallSource, hsv, ColorConversion.Bgr2Hsv);
        using Mat bgMask = CreateBackgroundMask(hsv, avgBgHsv, tolerance);

        int maxBezelX = Math.Clamp(scaledW / 30, 4, 30);
        int maxBezelY = Math.Clamp(scaledH / 30, 4, 30);

        int topBezel = 0;
        for (int y = 0; y < maxBezelY; y++)
        {
            using Mat row = new(bgMask, new Rectangle(0, y, scaledW, 1));
            int nonBg = scaledW - CvInvoke.CountNonZero(row);
            if (nonBg >= scaledW * 0.70)
            {
                topBezel = y + 1;
            }
            else if (nonBg < scaledW * 0.40)
            {
                break;
            }
        }
        if (topBezel >= maxBezelY) topBezel = 0; // Large object or photo touching boundary; not a thin bezel

        int bottomBezel = 0;
        for (int y = scaledH - 1; y >= scaledH - maxBezelY; y--)
        {
            using Mat row = new(bgMask, new Rectangle(0, y, scaledW, 1));
            int nonBg = scaledW - CvInvoke.CountNonZero(row);
            if (nonBg >= scaledW * 0.70)
            {
                bottomBezel = scaledH - y;
            }
            else if (nonBg < scaledW * 0.40)
            {
                break;
            }
        }
        if (bottomBezel >= maxBezelY) bottomBezel = 0;

        int leftBezel = 0;
        for (int x = 0; x < maxBezelX; x++)
        {
            using Mat col = new(bgMask, new Rectangle(x, 0, 1, scaledH));
            int nonBg = scaledH - CvInvoke.CountNonZero(col);
            if (nonBg >= scaledH * 0.70)
            {
                leftBezel = x + 1;
            }
            else if (nonBg < scaledH * 0.40)
            {
                break;
            }
        }
        if (leftBezel >= maxBezelX) leftBezel = 0;

        int rightBezel = 0;
        for (int x = scaledW - 1; x >= scaledW - maxBezelX; x--)
        {
            using Mat col = new(bgMask, new Rectangle(x, 0, 1, scaledH));
            int nonBg = scaledH - CvInvoke.CountNonZero(col);
            if (nonBg >= scaledH * 0.70)
            {
                rightBezel = scaledW - x;
            }
            else if (nonBg < scaledH * 0.40)
            {
                break;
            }
        }
        if (rightBezel >= maxBezelX) rightBezel = 0;

        double invScale = 1.0 / scale;
        return (
            (int)Math.Round(topBezel * invScale),
            (int)Math.Round(bottomBezel * invScale),
            (int)Math.Round(leftBezel * invScale),
            (int)Math.Round(rightBezel * invScale)
        );
    }
}
