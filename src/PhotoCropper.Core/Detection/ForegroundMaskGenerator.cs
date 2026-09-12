using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System.Drawing;

namespace PhotoCropper.Detection;

public static class ForegroundMaskGenerator
{
    public static Mat GeneratePrecomputedEdgeMap(Mat source, double lowThreshold, double highThreshold)
    {
        ArgumentNullException.ThrowIfNull(source);

        using Mat gray = new();
        CvInvoke.CvtColor(source, gray, ColorConversion.Bgr2Gray);

        // Bilateral filter smooths internal photo textures while preserving sharp outer boundaries
        using Mat smoothed = new();
        CvInvoke.BilateralFilter(gray, smoothed, 7, 50, 50);

        Mat edges = new();
        CvInvoke.Canny(smoothed, edges, lowThreshold, highThreshold);

        int minDim = Math.Min(source.Width, source.Height);

        // Seal faint low-contrast borders (e.g. white photo borders) using dynamic Adaptive Thresholding
        int adaptiveBlockSize = Math.Max(5, (minDim / 150) | 1); // Resolution-aware block size
        using Mat adaptive = new();
        CvInvoke.AdaptiveThreshold(smoothed, adaptive, 255, AdaptiveThresholdType.GaussianC, ThresholdType.BinaryInv, adaptiveBlockSize, 7);
        CvInvoke.BitwiseOr(edges, adaptive, edges);

        return edges;
    }

    public static void PopulateForegroundMask(
        Mat source,
        Mat outputForeground,
        MCvScalar avgBackgroundColorHsv,
        double backgroundTolerance,
        double lowThreshold,
        double highThreshold,
        Mat? precomputedEdgeMap = null,
        Mat? precomputedHsv = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(outputForeground);

        using Mat ownedHsv = precomputedHsv == null ? new Mat() : new Mat();
        Mat hsvToUse;
        if (precomputedHsv != null)
        {
            hsvToUse = precomputedHsv;
        }
        else
        {
            CvInvoke.CvtColor(source, ownedHsv, ColorConversion.Bgr2Hsv);
            hsvToUse = ownedHsv;
        }

        using Mat backgroundMask = BackgroundAnalyzer.CreateBackgroundMask(hsvToUse, avgBackgroundColorHsv, backgroundTolerance);

        using Mat ownedEdges = precomputedEdgeMap == null ? GeneratePrecomputedEdgeMap(source, lowThreshold, highThreshold) : new Mat();
        Mat edgesToUse = precomputedEdgeMap ?? ownedEdges;

        CvInvoke.BitwiseNot(backgroundMask, outputForeground);
        CvInvoke.BitwiseOr(outputForeground, edgesToUse, outputForeground);

        // Dynamically scale morphology kernels based on scan resolution/dimensions
        // Use an elliptical close kernel to seal internal edges without bridging narrow gaps between adjacent photos
        int minDim = Math.Min(source.Width, source.Height);
        int openSize = Math.Max(3, (minDim / 400) | 1);  // Ensure odd integer, min 3
        int closeSize = Math.Max(3, (minDim / 500) | 1); // Ensure odd integer, min 3

        using Mat openKernel = CvInvoke.GetStructuringElement(MorphShapes.Rectangle, new Size(openSize, openSize), new Point(-1, -1));
        CvInvoke.MorphologyEx(outputForeground, outputForeground, MorphOp.Open, openKernel, new Point(-1, -1), 1, BorderType.Default, new MCvScalar());

        using Mat closeKernel = CvInvoke.GetStructuringElement(MorphShapes.Ellipse, new Size(closeSize, closeSize), new Point(-1, -1));
        CvInvoke.MorphologyEx(outputForeground, outputForeground, MorphOp.Close, closeKernel, new Point(-1, -1), 1, BorderType.Default, new MCvScalar());
    }
}
