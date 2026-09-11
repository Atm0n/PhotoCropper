using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

namespace PhotoCropper.Detection;

public static class ForegroundMaskGenerator
{
    public static void PopulateForegroundMask(
        Mat source, 
        Mat outputForeground, 
        MCvScalar avgBackgroundColorHsv, 
        double backgroundTolerance, 
        double lowThreshold, 
        double highThreshold)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(outputForeground);

        using Mat hsv = new();
        CvInvoke.CvtColor(source, hsv, ColorConversion.Bgr2Hsv);

        using Mat backgroundMask = BackgroundAnalyzer.CreateBackgroundMask(hsv, avgBackgroundColorHsv, backgroundTolerance);

        using Mat gray = new();
        CvInvoke.CvtColor(source, gray, ColorConversion.Bgr2Gray);
        
        // Bilateral filter smooths internal photo textures while preserving sharp outer boundaries
        using Mat smoothed = new();
        CvInvoke.BilateralFilter(gray, smoothed, 9, 75, 75);
        
        using Mat edges = new();
        CvInvoke.Canny(smoothed, edges, lowThreshold, highThreshold);

        int minDim = Math.Min(source.Width, source.Height);

        // Seal faint low-contrast borders (e.g. white photo borders) using dynamic Adaptive Thresholding
        int adaptiveBlockSize = Math.Max(5, (minDim / 150) | 1); // Resolution-aware block size
        using Mat adaptive = new();
        CvInvoke.AdaptiveThreshold(smoothed, adaptive, 255, AdaptiveThresholdType.GaussianC, ThresholdType.BinaryInv, adaptiveBlockSize, 7);
        CvInvoke.BitwiseOr(edges, adaptive, edges);

        CvInvoke.BitwiseNot(backgroundMask, outputForeground);
        CvInvoke.BitwiseOr(outputForeground, edges, outputForeground);

        // Dynamically scale morphology kernels based on scan resolution/dimensions
        // Use an elliptical close kernel to seal internal edges without bridging narrow gaps between adjacent photos
        int openSize = Math.Max(3, (minDim / 400) | 1);  // Ensure odd integer, min 3
        int closeSize = Math.Max(3, (minDim / 500) | 1); // Ensure odd integer, min 3

        using Mat openKernel = CvInvoke.GetStructuringElement(MorphShapes.Rectangle, new Size(openSize, openSize), new Point(-1, -1));
        CvInvoke.MorphologyEx(outputForeground, outputForeground, MorphOp.Open, openKernel, new Point(-1, -1), 1, BorderType.Default, new MCvScalar());

        using Mat closeKernel = CvInvoke.GetStructuringElement(MorphShapes.Ellipse, new Size(closeSize, closeSize), new Point(-1, -1));
        CvInvoke.MorphologyEx(outputForeground, outputForeground, MorphOp.Close, closeKernel, new Point(-1, -1), 1, BorderType.Default, new MCvScalar());
    }
}
