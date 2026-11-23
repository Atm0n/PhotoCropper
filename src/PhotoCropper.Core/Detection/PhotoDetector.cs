using OpenCvSharp;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Models;

namespace PhotoCropper.Core.Detection;

/// <summary>
/// The scan prepared for detection: padded with the background colour (so photos touching the scan's edge still
/// form closed shapes), with the scanner's bezel painted over. Photos reaching past the edge are extracted from it.
/// </summary>
internal sealed class DetectionImage : IDisposable
{
    public required Mat Padded { get; init; }
    public required Scalar BackgroundHsv { get; init; }
    public required Scalar BackgroundBgr { get; init; }

    /// <summary>The padding added on each side, in scan pixels.</summary>
    public required int Pad { get; init; }

    public void Dispose() => Padded.Dispose();
}

/// <summary>Finds the boxes of the photos on a scan: the automatic detection behind Detect and the CLI.</summary>
internal static class PhotoDetector
{
    /// <summary>Long side, in pixels, the padded scan is shrunk to for finding shapes (boxes are mapped back).</summary>
    private const int TargetMaxDim = 1600;

    /// <summary>Pads <paramref name="original"/> (BGR) and paints its bezel over; see <see cref="DetectionImage"/>.</summary>
    public static DetectionImage Prepare(Mat original, DetectionOptions options)
    {
        // Sample background color before padding
        Scalar backgroundHsv;
        Scalar? lightPaper = null;
        using (Mat hsv = new())
        {
            Cv2.CvtColor(original, hsv, ColorConversionCodes.BGR2HSV);
            backgroundHsv = options.CustomBackgroundColorHsv is { } picked ? CvInterop.ToCv(picked) : BackgroundAnalyzer.SampleBackgroundColor(hsv);
            // A light album page on the bed: its paper is the background, as if picked with Pick Color.
            if (options.CustomBackgroundColorHsv == null) lightPaper = BackgroundAnalyzer.LightPageColour(hsv, backgroundHsv);
            if (lightPaper is { } paper) backgroundHsv = paper;
        }

        // An album page on a light bed: the bed around it is background too, and the page's colour (unless one was
        // picked, or is a light page's paper) is the one inside its edge.
        using Mat? aroundPage = BackgroundAnalyzer.FindBedAroundPage(original, backgroundHsv, out Scalar background);
        if (options.CustomBackgroundColorHsv == null && lightPaper == null) backgroundHsv = background;

        // Convert HSV background color to BGR for border padding
        Scalar backgroundBgr = BackgroundAnalyzer.HsvToBgr(backgroundHsv);

        // Pad the full image with a synthetic margin so photos touching or extending to the scan boundary form complete closed contours
        int pad = Math.Max(20, Math.Min(original.Width, original.Height) / 50);
        Mat padded = new();
        Cv2.CopyMakeBorder(original, padded, pad, pad, pad, pad, BorderTypes.Constant, backgroundBgr);

        // Neutralize scanner bezel / platen border margins in padded detection image
        var margins = BackgroundAnalyzer.DetectBezelMargins(original, backgroundHsv, options.BackgroundTolerance);
        BackgroundAnalyzer.PaintBezels(padded, margins, pad, backgroundBgr);

        if (aroundPage != null)
        {
            using Mat scanArea = new(padded, new Rect(pad, pad, original.Width, original.Height));
            scanArea.SetTo(backgroundBgr, aroundPage);
        }

        return new DetectionImage { Padded = padded, BackgroundHsv = backgroundHsv, BackgroundBgr = backgroundBgr, Pad = pad };
    }

    /// <summary>
    /// The photos' boxes on <paramref name="original"/>, in scan pixels: shapes found at several background
    /// tolerances around the chosen one (or by brightness alone when none is found), with composites and overlaps resolved.
    /// </summary>
    public static IReadOnlyList<CropCandidate> FindCandidates(Mat original, DetectionImage image, DetectionOptions options, bool lookInsidePages = true)
    {
        Mat padded = image.Padded;

        // Determine downscale factor for ultra-fast contour detection on high-DPI scans
        int maxDim = Math.Max(padded.Width, padded.Height);
        double scale = 1.0;

        using Mat scaledDetectionMat = new();
        Mat detectionMat;

        if (maxDim > TargetMaxDim)
        {
            scale = (double)TargetMaxDim / maxDim;
            int scaledW = (int)Math.Round(padded.Width * scale);
            int scaledH = (int)Math.Round(padded.Height * scale);
            Cv2.Resize(padded, scaledDetectionMat, new Size(scaledW, scaledH), 0, 0, InterpolationFlags.Area);
            detectionMat = scaledDetectionMat;
        }
        else
        {
            detectionMat = padded;
        }

        int scaledPad = (int)Math.Round(image.Pad * scale);
        int originalW = original.Width;
        int originalH = original.Height;

        // Precompute HSV and Edge maps once on the detection resolution
        using Mat detHsv = new();
        Cv2.CvtColor(detectionMat, detHsv, ColorConversionCodes.BGR2HSV);

        using Mat precomputedEdges = ForegroundMaskGenerator.GeneratePrecomputedEdgeMap(
            detectionMat,
            options.CannyLowThreshold,
            options.CannyHighThreshold);

        // Multi-pass sensitivity detection:
        // Evaluates balanced search tolerances around base tolerance (both tighter and wider)
        var candidateDetections = new List<CropCandidate>();
        double baseTol = options.BackgroundTolerance;
        double[] searchTolerances = [
            baseTol,
            baseTol + 4,
            baseTol + 8,
            baseTol + 15,
            Math.Max(5, baseTol - 6),
            Math.Max(2, baseTol - 12)
        ];

        // The passes are independent, so they run side by side; their candidates are then gathered in the passes'
        // order, so what follows sees exactly the same list as when they ran one after another.
        var passes = new IReadOnlyList<CropCandidate>[searchTolerances.Length];
        Parallel.For(0, searchTolerances.Length, i =>
        {
            using Mat foreground = new();
            ForegroundMaskGenerator.PopulateForegroundMask(
                detectionMat,
                foreground,
                image.BackgroundHsv,
                searchTolerances[i],
                options.CannyLowThreshold,
                options.CannyHighThreshold,
                precomputedEdges,
                detHsv);

            passes[i] = CandidateExtractor.ExtractCandidates(
                foreground,
                scaledPad,
                (int)Math.Round(originalW * scale),
                (int)Math.Round(originalH * scale),
                options.MinAreaFactor,
                options.MaxAreaFactor,
                detectionMat,
                image.BackgroundBgr);
        });
        foreach (var passCandidates in passes)
        {
            MapCandidatesToOriginalSpace(candidateDetections, passCandidates, scale, originalW, originalH);
        }

        // Automatic Otsu Fallback: If HSV background subtraction failed to find any photos
        // (common with faint vintage prints, light skies, or low-contrast scan lids),
        // run an automatic global luminance Otsu segmentation pass
        if (candidateDetections.Count == 0)
        {
            bool isLightBg = image.BackgroundHsv.Val2 > 120;
            using Mat otsuForeground = new();
            ForegroundMaskGenerator.PopulateOtsuForegroundMask(
                detectionMat,
                otsuForeground,
                options.CannyLowThreshold,
                options.CannyHighThreshold,
                isLightBg,
                precomputedEdges);

            var otsuCandidates = CandidateExtractor.ExtractCandidates(
                otsuForeground,
                scaledPad,
                (int)Math.Round(originalW * scale),
                (int)Math.Round(originalH * scale),
                options.MinAreaFactor,
                options.MaxAreaFactor,
                detectionMat,
                image.BackgroundBgr);

            MapCandidatesToOriginalSpace(candidateDetections, otsuCandidates, scale, originalW, originalH);
        }

        // Composite resolution and overlap filtering
        var filtered = CandidateResolutionFilter.FilterCandidates(candidateDetections);

        // An album page taken whole (its photos too close to its colour): its photos instead.
        return lookInsidePages ? PageSplitter.Split(original, filtered, options) : filtered;
    }

    private static void MapCandidatesToOriginalSpace(
        List<CropCandidate> destination,
        IReadOnlyList<CropCandidate> candidates,
        double scale,
        int originalW,
        int originalH)
    {
        if (scale < 0.999)
        {
            double invScale = 1.0 / scale;
            foreach (var cand in candidates)
            {
                Point2f fullCenter = new((float)(cand.Rotated.Center.X * invScale), (float)(cand.Rotated.Center.Y * invScale));
                Size2f fullSize = new((float)(cand.Rotated.Size.Width * invScale), (float)(cand.Rotated.Size.Height * invScale));
                RotatedRect fullRr = new(fullCenter, fullSize, cand.Rotated.Angle);

                Point2f[] fullVerts = fullRr.Points();
                Point[] fullPoints = new Point[4];
                for (int p = 0; p < 4; p++)
                {
                    fullPoints[p] = new Point(
                        Math.Clamp((int)Math.Round(fullVerts[p].X), 0, originalW - 1),
                        Math.Clamp((int)Math.Round(fullVerts[p].Y), 0, originalH - 1)
                    );
                }

                double fullArea = (double)fullSize.Width * fullSize.Height;
                double score = fullArea * Math.Pow(cand.Rectangularity, 3) * Math.Pow(cand.Convexity, 2);

                destination.Add(new CropCandidate(
                    CvInterop.ToPoints(fullPoints),
                    CvInterop.ToRectangle(Cv2.BoundingRect(fullPoints)),
                    score,
                    CvInterop.ToBox(fullRr),
                    fullArea,
                    cand.Rectangularity,
                    cand.Convexity));
            }
        }
        else
        {
            destination.AddRange(candidates);
        }
    }
}
