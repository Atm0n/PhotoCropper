using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System.Drawing;

namespace PhotoCropper.Core.Detection;

public static class ForegroundMaskGenerator
{
    public static Mat GeneratePrecomputedEdgeMap(Mat source, double lowThreshold, double highThreshold)
    {
        ArgumentNullException.ThrowIfNull(source);

        using Mat gray = new();
        if (source.NumberOfChannels == 1)
        {
            source.CopyTo(gray);
        }
        else if (source.NumberOfChannels == 4)
        {
            CvInvoke.CvtColor(source, gray, ColorConversion.Bgra2Gray);
        }
        else
        {
            CvInvoke.CvtColor(source, gray, ColorConversion.Bgr2Gray);
        }

        // Bilateral filter smooths internal photo textures while preserving sharp outer boundaries
        using Mat smoothed = new();
        CvInvoke.BilateralFilter(gray, smoothed, 7, 50, 50);

        Mat edges = new();
        CvInvoke.Canny(smoothed, edges, lowThreshold, highThreshold);

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
            if (source.NumberOfChannels == 1)
            {
                using Mat bgrSource = new();
                CvInvoke.CvtColor(source, bgrSource, ColorConversion.Gray2Bgr);
                CvInvoke.CvtColor(bgrSource, ownedHsv, ColorConversion.Bgr2Hsv);
            }
            else if (source.NumberOfChannels == 4)
            {
                using Mat bgrSource = new();
                CvInvoke.CvtColor(source, bgrSource, ColorConversion.Bgra2Bgr);
                CvInvoke.CvtColor(bgrSource, ownedHsv, ColorConversion.Bgr2Hsv);
            }
            else
            {
                CvInvoke.CvtColor(source, ownedHsv, ColorConversion.Bgr2Hsv);
            }
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

        using Mat openKernel = CvInvoke.GetStructuringElement(MorphShapes.Ellipse, new Size(openSize, openSize), new Point(-1, -1));
        CvInvoke.MorphologyEx(outputForeground, outputForeground, MorphOp.Open, openKernel, new Point(-1, -1), 1, BorderType.Default, new MCvScalar());

        using Mat closeKernel = CvInvoke.GetStructuringElement(MorphShapes.Ellipse, new Size(closeSize, closeSize), new Point(-1, -1));
        CvInvoke.MorphologyEx(outputForeground, outputForeground, MorphOp.Close, closeKernel, new Point(-1, -1), 1, BorderType.Default, new MCvScalar());
    }

    public static void PopulateOtsuForegroundMask(
        Mat source,
        Mat outputForeground,
        double lowThreshold,
        double highThreshold,
        bool isLightBackground = true,
        Mat? precomputedEdgeMap = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(outputForeground);

        using Mat gray = new();
        if (source.NumberOfChannels == 1)
        {
            source.CopyTo(gray);
        }
        else if (source.NumberOfChannels == 4)
        {
            CvInvoke.CvtColor(source, gray, ColorConversion.Bgra2Gray);
        }
        else
        {
            CvInvoke.CvtColor(source, gray, ColorConversion.Bgr2Gray);
        }

        using Mat otsuMask = new();
        var threshType = isLightBackground
            ? (ThresholdType.BinaryInv | ThresholdType.Otsu)
            : (ThresholdType.Binary | ThresholdType.Otsu);

        CvInvoke.Threshold(gray, otsuMask, 0, 255, threshType);

        using Mat ownedEdges = precomputedEdgeMap == null ? GeneratePrecomputedEdgeMap(source, lowThreshold, highThreshold) : new Mat();
        Mat edgesToUse = precomputedEdgeMap ?? ownedEdges;

        CvInvoke.BitwiseOr(otsuMask, edgesToUse, outputForeground);

        int minDim = Math.Min(source.Width, source.Height);
        int openSize = Math.Max(3, (minDim / 400) | 1);
        int closeSize = Math.Max(3, (minDim / 500) | 1);

        using Mat openKernel = CvInvoke.GetStructuringElement(MorphShapes.Ellipse, new Size(openSize, openSize), new Point(-1, -1));
        CvInvoke.MorphologyEx(outputForeground, outputForeground, MorphOp.Open, openKernel, new Point(-1, -1), 1, BorderType.Default, new MCvScalar());

        using Mat closeKernel = CvInvoke.GetStructuringElement(MorphShapes.Ellipse, new Size(closeSize, closeSize), new Point(-1, -1));
        CvInvoke.MorphologyEx(outputForeground, outputForeground, MorphOp.Close, closeKernel, new Point(-1, -1), 1, BorderType.Default, new MCvScalar());
    }
}
