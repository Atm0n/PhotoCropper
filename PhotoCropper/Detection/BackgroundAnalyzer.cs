using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

namespace PhotoCropper.Detection;

public static class BackgroundAnalyzer
{
    public static MCvScalar SampleBackgroundColor(Mat hsv)
    {
        ArgumentNullException.ThrowIfNull(hsv);
        int s = 15; // sample box size
        if (hsv.Width < s * 3 + 20 || hsv.Height < s * 3 + 20) return new MCvScalar();

        int halfW = hsv.Width / 2;
        int halfH = hsv.Height / 2;

        var rects = new Rectangle[]
        {
            new(5, 5, s, s),                               // Top-Left
            new(halfW - s / 2, 5, s, s),                   // Top-Center
            new(hsv.Width - s - 5, 5, s, s),               // Top-Right
            new(5, halfH - s / 2, s, s),                   // Middle-Left
            new(hsv.Width - s - 5, halfH - s / 2, s, s),   // Middle-Right
            new(5, hsv.Height - s - 5, s, s),              // Bottom-Left
            new(halfW - s / 2, hsv.Height - s - 5, s, s),  // Bottom-Center
            new(hsv.Width - s - 5, hsv.Height - s - 5, s, s) // Bottom-Right
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
        using Mat hsvSampled = new();
        CvInvoke.CvtColor(sampledArea, hsvSampled, ColorConversion.Bgr2Hsv);
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
        double hTol = tolerance * 0.4;
        double sTol = tolerance;
        
        // Widen Value (V) tolerance slightly to absorb scanner lid shadow gradients if the background is light
        double vTol = avgColor.V2 > 128 ? tolerance * 1.5 : tolerance;

        MCvScalar lower = new(Math.Max(0, avgColor.V0 - hTol), Math.Max(0, avgColor.V1 - sTol), Math.Max(0, avgColor.V2 - vTol));
        MCvScalar upper = new(Math.Min(180, avgColor.V0 + hTol), Math.Min(255, avgColor.V1 + sTol), Math.Min(255, avgColor.V2 + vTol));

        Mat mask = new();
        using ScalarArray lowerArray = new(lower);
        using ScalarArray upperArray = new(upper);
        CvInvoke.InRange(hsv, lowerArray, upperArray, mask);
        return mask;
    }
}
