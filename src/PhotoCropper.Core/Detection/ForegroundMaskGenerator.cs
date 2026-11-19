using OpenCvSharp;

namespace PhotoCropper.Core.Detection;

public static class ForegroundMaskGenerator
{
    public static Mat GeneratePrecomputedEdgeMap(Mat source, double lowThreshold, double highThreshold)
    {
        ArgumentNullException.ThrowIfNull(source);

        using Mat gray = new();
        if (source.Channels() == 1)
        {
            source.CopyTo(gray);
        }
        else if (source.Channels() == 4)
        {
            Cv2.CvtColor(source, gray, ColorConversionCodes.BGRA2GRAY);
        }
        else
        {
            Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
        }

        // Bilateral filter smooths internal photo textures while preserving sharp outer boundaries
        using Mat smoothed = new();
        Cv2.BilateralFilter(gray, smoothed, 7, 50, 50);

        Mat edges = new();
        Cv2.Canny(smoothed, edges, lowThreshold, highThreshold);

        return edges;
    }

    public static void PopulateForegroundMask(
        Mat source,
        Mat outputForeground,
        Scalar avgBackgroundColorHsv,
        double backgroundTolerance,
        double lowThreshold,
        double highThreshold,
        Mat? precomputedEdgeMap = null,
        Mat? precomputedHsv = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(outputForeground);

        using Mat? ownedHsv = precomputedHsv == null ? new Mat() : null;
        Mat hsvToUse;
        if (precomputedHsv != null)
        {
            hsvToUse = precomputedHsv;
        }
        else
        {
            Mat targetHsv = ownedHsv!;
            if (source.Channels() == 1)
            {
                using Mat bgrSource = new();
                Cv2.CvtColor(source, bgrSource, ColorConversionCodes.GRAY2BGR);
                Cv2.CvtColor(bgrSource, targetHsv, ColorConversionCodes.BGR2HSV);
            }
            else if (source.Channels() == 4)
            {
                using Mat bgrSource = new();
                Cv2.CvtColor(source, bgrSource, ColorConversionCodes.BGRA2BGR);
                Cv2.CvtColor(bgrSource, targetHsv, ColorConversionCodes.BGR2HSV);
            }
            else
            {
                Cv2.CvtColor(source, targetHsv, ColorConversionCodes.BGR2HSV);
            }
            hsvToUse = targetHsv;
        }

        using Mat backgroundMask = BackgroundAnalyzer.CreateBackgroundMask(hsvToUse, avgBackgroundColorHsv, backgroundTolerance);

        using Mat? ownedEdges = precomputedEdgeMap == null ? GeneratePrecomputedEdgeMap(source, lowThreshold, highThreshold) : null;
        Mat edgesToUse = precomputedEdgeMap ?? ownedEdges!;

        Cv2.BitwiseNot(backgroundMask, outputForeground);
        Cv2.BitwiseOr(outputForeground, edgesToUse, outputForeground);

        // Dynamically scale morphology kernels based on scan resolution/dimensions
        // Use an elliptical close kernel to seal internal edges without bridging narrow gaps between adjacent photos
        int minDim = Math.Min(source.Width, source.Height);
        int openSize = Math.Max(3, (minDim / 400) | 1);  // Ensure odd integer, min 3
        int closeSize = Math.Max(3, (minDim / 500) | 1); // Ensure odd integer, min 3

        using Mat openKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(openSize, openSize), new Point(-1, -1));
        Cv2.MorphologyEx(outputForeground, outputForeground, MorphTypes.Open, openKernel, new Point(-1, -1), 1, BorderTypes.Default, new Scalar());

        using Mat closeKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(closeSize, closeSize), new Point(-1, -1));
        Cv2.MorphologyEx(outputForeground, outputForeground, MorphTypes.Close, closeKernel, new Point(-1, -1), 1, BorderTypes.Default, new Scalar());

        // Fill internal holes in detected photo contours so light internal regions
        // (white shirts, pale skies, clouds, faces) do not hollow out the photo or degrade rectangularity
        Cv2.FindContours(outputForeground, out Point[][] externalContours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        double minHoleFillArea = (double)source.Width * source.Height * 0.005; // 0.5% min area
        for (int i = 0; i < externalContours.Length; i++)
        {
            if (Cv2.ContourArea(externalContours[i]) >= minHoleFillArea)
            {
                Cv2.DrawContours(outputForeground, [externalContours[i]], 0, new Scalar(255), -1);
            }
        }
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
        if (source.Channels() == 1)
        {
            source.CopyTo(gray);
        }
        else if (source.Channels() == 4)
        {
            Cv2.CvtColor(source, gray, ColorConversionCodes.BGRA2GRAY);
        }
        else
        {
            Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
        }

        using Mat otsuMask = new();
        var threshType = isLightBackground
            ? (ThresholdTypes.BinaryInv | ThresholdTypes.Otsu)
            : (ThresholdTypes.Binary | ThresholdTypes.Otsu);

        Cv2.Threshold(gray, otsuMask, 0, 255, threshType);

        using Mat ownedEdges = precomputedEdgeMap == null ? GeneratePrecomputedEdgeMap(source, lowThreshold, highThreshold) : new Mat();
        Mat edgesToUse = precomputedEdgeMap ?? ownedEdges;

        Cv2.BitwiseOr(otsuMask, edgesToUse, outputForeground);

        int minDim = Math.Min(source.Width, source.Height);
        int openSize = Math.Max(3, (minDim / 400) | 1);
        int closeSize = Math.Max(3, (minDim / 500) | 1);

        using Mat openKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(openSize, openSize), new Point(-1, -1));
        Cv2.MorphologyEx(outputForeground, outputForeground, MorphTypes.Open, openKernel, new Point(-1, -1), 1, BorderTypes.Default, new Scalar());

        using Mat closeKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(closeSize, closeSize), new Point(-1, -1));
        Cv2.MorphologyEx(outputForeground, outputForeground, MorphTypes.Close, closeKernel, new Point(-1, -1), 1, BorderTypes.Default, new Scalar());

        Cv2.FindContours(outputForeground, out Point[][] otsuExternalContours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        double minHoleFillArea = (double)source.Width * source.Height * 0.005;
        for (int i = 0; i < otsuExternalContours.Length; i++)
        {
            if (Cv2.ContourArea(otsuExternalContours[i]) >= minHoleFillArea)
            {
                Cv2.DrawContours(outputForeground, [otsuExternalContours[i]], 0, new Scalar(255), -1);
            }
        }
    }
}
