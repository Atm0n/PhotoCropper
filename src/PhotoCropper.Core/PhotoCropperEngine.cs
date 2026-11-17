using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using PhotoCropper.Core.Detection;
using PhotoCropper.Core.Export;
using PhotoCropper.Core.Extraction;
using PhotoCropper.Core.Models;
using PhotoCropper.Core.Workspace;
using System.Collections.ObjectModel;
using System.Drawing;

namespace PhotoCropper.Core;

public class PhotoCropperEngine : IDisposable
{
    static PhotoCropperEngine()
    {
        CvInvoke.LogLevel = LogLevel.Error;
    }

    private bool disposedValue;

    public string OriginalFilePath { get; }

    // Configurable Detection Parameters
    public double BackgroundTolerance { get; set; } = Common.AppConstants.DefaultBackgroundTolerance;
    public double MinAreaFactor { get; set; } = Common.AppConstants.DefaultMinAreaFactor;
    public double MaxAreaFactor { get; set; } = Common.AppConstants.DefaultMaxAreaFactor;
    public double CannyLowThreshold { get; set; } = Common.AppConstants.DefaultCannyLow;
    public double CannyHighThreshold { get; set; } = Common.AppConstants.DefaultCannyHigh;
    public MCvScalar? CustomBackgroundColorHsv { get; set; }
    public bool AutoOrientPhotos { get; set; } = true;
    public bool RestoreVintageColors { get; set; }
    public bool RemoveDustAndScratches { get; set; }
    public string BoundingBoxColor { get; set; } = Common.AppConstants.DefaultBoundingBoxColor;

    public Mat Original { get; set; }
    public Mat OriginalWithDetected { get; set; }
    public Collection<Mat> DetectedPhotos { get; } = [];
    public Collection<Mat> RawDetectedPhotos { get; } = [];
    public Dictionary<int, int> PhotoRotations { get; } = [];
    public IReadOnlyList<CropCandidate> AcceptedCandidates { get; private set; } = [];

    public PhotoCropperEngine(string originalFilePath)
    {
        ArgumentNullException.ThrowIfNull(originalFilePath);
        if (!File.Exists(originalFilePath))
        {
            throw new FileNotFoundException("Scan file not found.", originalFilePath);
        }

        var fileInfo = new FileInfo(originalFilePath);
        if (fileInfo.Length == 0)
        {
            throw new InvalidOperationException($"The file '{originalFilePath}' is empty (0 bytes).");
        }

        OriginalFilePath = originalFilePath;
        using var stream = new FileStream(originalFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var ms = new MemoryStream((int)stream.Length);
        stream.CopyTo(ms);
        byte[] fileBytes = ms.ToArray();
        using Mat rawMat = new();
        CvInvoke.Imdecode(fileBytes, ImreadModes.AnyColor, rawMat);
        if (rawMat.IsEmpty || rawMat.Width <= 0 || rawMat.Height <= 0)
        {
            throw new InvalidOperationException($"Failed to decode image from file '{originalFilePath}'. The image format may be invalid or corrupt.");
        }
        Original = NormalizeToBgr(rawMat);
        OriginalWithDetected = Original.Clone();
    }

    public static Mat NormalizeToBgr(Mat source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.IsEmpty || source.Width <= 0 || source.Height <= 0)
        {
            return new Mat();
        }

        if (source.NumberOfChannels == 3)
        {
            return source.Clone();
        }

        Mat bgr = new();
        if (source.NumberOfChannels == 1)
        {
            CvInvoke.CvtColor(source, bgr, ColorConversion.Gray2Bgr);
        }
        else if (source.NumberOfChannels == 4)
        {
            CvInvoke.CvtColor(source, bgr, ColorConversion.Bgra2Bgr);
        }
        else
        {
            source.CopyTo(bgr);
        }

        return bgr;
    }



    public void ApplyOptions(DetectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        BackgroundTolerance = options.BackgroundTolerance;
        MinAreaFactor = options.MinAreaFactor;
        MaxAreaFactor = options.MaxAreaFactor;
        CannyLowThreshold = options.CannyLowThreshold;
        CannyHighThreshold = options.CannyHighThreshold;
        CustomBackgroundColorHsv = options.CustomBackgroundColorHsv;
        AutoOrientPhotos = options.AutoOrientPhotos;
        RestoreVintageColors = options.RestoreVintageColors;
        RemoveDustAndScratches = options.RemoveDustAndScratches;
        BoundingBoxColor = options.BoundingBoxColor;
    }

    public DetectionOptions CurrentOptions => new()
    {
        BackgroundTolerance = BackgroundTolerance,
        MinAreaFactor = MinAreaFactor,
        MaxAreaFactor = MaxAreaFactor,
        CannyLowThreshold = CannyLowThreshold,
        CannyHighThreshold = CannyHighThreshold,
        CustomBackgroundColorHsv = CustomBackgroundColorHsv,
        AutoOrientPhotos = AutoOrientPhotos,
        RestoreVintageColors = RestoreVintageColors,
        RemoveDustAndScratches = RemoveDustAndScratches,
        BoundingBoxColor = BoundingBoxColor
    };

    public AutoTuneResult AutoTune(int minExpected = 1, int maxExpected = int.MaxValue)
    {
        var tuneResult = AutoTuneService.Tune(Original, CurrentOptions, minExpected, maxExpected);
        ApplyOptions(tuneResult.BestOptions);
        DetectPhotos();
        return tuneResult;
    }

    public double TotalDetectedAreaRatio
    {
        get
        {
            if (Original.IsEmpty || Original.Width <= 0 || Original.Height <= 0) return 0.0;
            double totalArea = (double)Original.Width * Original.Height;
            double coveredArea = 0.0;
            foreach (var candidate in AcceptedCandidates)
            {
                coveredArea += candidate.Area;
            }
            return Math.Clamp(coveredArea / totalArea, 0.0, 1.0);
        }
    }

    private void ResetState()
    {
        foreach (var photo in DetectedPhotos) photo.Dispose();
        DetectedPhotos.Clear();

        foreach (var raw in RawDetectedPhotos) raw.Dispose();
        RawDetectedPhotos.Clear();

        // Rotations are keyed by photo index, so they belong to the photos being discarded here.
        // Keeping them would apply stale user rotations to newly detected photos.
        PhotoRotations.Clear();

        OriginalWithDetected?.Dispose();
        OriginalWithDetected = Original.Clone();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                Original?.Dispose();
                OriginalWithDetected?.Dispose();
                foreach (var photo in DetectedPhotos) photo.Dispose();
                DetectedPhotos.Clear();
                foreach (var raw in RawDetectedPhotos) raw.Dispose();
                RawDetectedPhotos.Clear();
            }
            disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public void RestoreFromSavedCrops(IEnumerable<WorkspaceCropData> savedCrops)
    {
        ArgumentNullException.ThrowIfNull(savedCrops);

        ResetState();
        if (Original == null || Original.IsEmpty || Original.Width <= 0 || Original.Height <= 0)
        {
            return;
        }

        var savedList = savedCrops as IReadOnlyList<WorkspaceCropData> ?? [.. savedCrops];

        var candidates = new List<CropCandidate>();
        var knownOrientations = new List<int?>();
        foreach (var crop in savedList)
        {
            var center = new PointF(crop.CenterX, crop.CenterY);
            var size = new SizeF(crop.Width, crop.Height);
            var rr = new RotatedRect(center, size, crop.Angle);
            candidates.Add(new CropCandidate { Rotated = rr, Area = crop.Width * crop.Height, ShapePoints = [], Rect = new Rectangle(0, 0, (int)crop.Width, (int)crop.Height) });
            knownOrientations.Add(crop.AutoRotationDegrees);
        }

        // Saved crops whose extraction comes back empty are dropped, so map each kept photo back to
        // the saved crop it came from rather than assuming photo i == saved crop i.
        var kept = ExtractAndProcessCandidates(candidates, knownOrientations: knownOrientations);
        AcceptedCandidates = [.. kept.Select(k => k.Candidate)];
        DrawBoundingBoxes(AcceptedCandidates);

        for (int photoIdx = 0; photoIdx < kept.Count; photoIdx++)
        {
            int rotationDegrees = NormalizeRightAngle(savedList[kept[photoIdx].SourceIndex].RotationDegrees);
            for (int r = 0; r < rotationDegrees / 90; r++)
            {
                RotatePhoto(photoIdx);
            }
        }
    }

    private static int NormalizeRightAngle(int degrees) => ((degrees % 360) + 360) % 360 / 90 * 90;

    private static RotateFlags ToRotateFlags(int clockwiseDegrees) => clockwiseDegrees switch
    {
        90 => RotateFlags.Rotate90Clockwise,
        180 => RotateFlags.Rotate180,
        _ => RotateFlags.Rotate90CounterClockwise
    };

    /// <summary>
    /// Runs the per-photo pipeline on a freshly extracted crop: auto-orientation, the user's manual rotation,
    /// the raw compare copy, then colour restoration. Takes ownership of <paramref name="extracted"/>.
    /// </summary>
    /// <param name="knownOrientation">Orientation to reuse instead of detecting it (e.g. from a saved session).</param>
    /// <returns>The processed photo, its raw (pre-restoration) copy, and the auto-orientation applied.</returns>
    private (Mat Photo, Mat Raw, int Orientation) ProcessExtractedPhoto(Mat extracted, int? knownOrientation = null, int userRotation = 0)
    {
        int orientation = 0;
        if (AutoOrientPhotos)
        {
            orientation = NormalizeRightAngle(knownOrientation ?? AutoOrientationService.DetectRequiredRotation(extracted));
        }

        int totalRotation = NormalizeRightAngle(orientation + userRotation);
        if (totalRotation != 0)
        {
            Mat rotated = new();
            CvInvoke.Rotate(extracted, rotated, ToRotateFlags(totalRotation));
            extracted.Dispose();
            extracted = rotated;
        }

        // The raw copy (used by hold-to-compare) must be taken before colour restoration.
        Mat raw = extracted.Clone();

        if (RestoreVintageColors)
        {
            Mat restored = PhotoRestorationService.RestoreColors(extracted, removeDust: RemoveDustAndScratches);
            if (!ReferenceEquals(restored, extracted))
            {
                extracted.Dispose();
                extracted = restored;
            }
        }

        return (extracted, raw, orientation);
    }

    /// <summary>
    /// Extracts every candidate in parallel and appends the non-empty results to
    /// <see cref="DetectedPhotos"/> / <see cref="RawDetectedPhotos"/>.
    /// </summary>
    /// <param name="knownOrientations">Optional per-candidate orientation to reuse instead of detecting it.</param>
    /// <returns>
    /// For each candidate that produced a photo, in order: its index in <paramref name="candidates"/> and the
    /// candidate with <see cref="CropCandidate.OrientationDegrees"/> filled in. Callers must use these to keep
    /// <see cref="AcceptedCandidates"/> aligned with <see cref="DetectedPhotos"/>.
    /// </returns>
    private List<(int SourceIndex, CropCandidate Candidate)> ExtractAndProcessCandidates(
        IReadOnlyList<CropCandidate> candidates,
        Mat? padded = null,
        int pad = 0,
        List<int?>? knownOrientations = null)
    {
        Mat?[] results = new Mat[candidates.Count];
        Mat?[] rawResults = new Mat[candidates.Count];
        int[] orientations = new int[candidates.Count];

        try
        {
            System.Threading.Tasks.Parallel.For(0, candidates.Count, i =>
            {
                Mat extracted = (padded != null && !padded.IsEmpty && pad > 0)
                    ? PhotoExtractionEngine.ExtractPhotoFromRotatedRect(candidates[i].Rotated, Original, padded, pad)
                    : PhotoExtractionEngine.ExtractPhotoFromRotatedRect(candidates[i].Rotated, Original);

                if (extracted.IsEmpty)
                {
                    extracted.Dispose();
                    return;
                }

                int? known = knownOrientations != null && i < knownOrientations.Count ? knownOrientations[i] : null;
                (results[i], rawResults[i], orientations[i]) = ProcessExtractedPhoto(extracted, known);
            });
        }
        catch
        {
            // One failing extraction aborts the loop; free whatever the other workers produced.
            foreach (var mat in results) mat?.Dispose();
            foreach (var raw in rawResults) raw?.Dispose();
            throw;
        }

        var kept = new List<(int, CropCandidate)>(candidates.Count);
        for (int i = 0; i < results.Length; i++)
        {
            var mat = results[i];
            var raw = rawResults[i];
            if (mat != null && !mat.IsEmpty && raw != null && !raw.IsEmpty)
            {
                DetectedPhotos.Add(mat);
                RawDetectedPhotos.Add(raw);
                kept.Add((i, candidates[i] with { OrientationDegrees = orientations[i] }));
            }
            else
            {
                mat?.Dispose();
                raw?.Dispose();
            }
        }

        return kept;
    }


    /// <summary>Long side, in pixels, at which overlay elements are drawn at their base size (≈ A4 at 300 DPI).</summary>
    private const double OverlayReferenceLongSide = 3500.0;

    /// <summary>Base distance from a box's top edge to its rotation handle, before <see cref="OverlayScale"/>.</summary>
    public const float RotationHandleBaseOffset = 60f;

    /// <summary>
    /// Factor applied to overlay sizes (lines, handles, badges) and to the matching hit-test distances, so they
    /// keep the same size relative to the scan. Fixed pixel sizes looked huge on 150 DPI scans and tiny at 1200 DPI.
    /// </summary>
    public double OverlayScale =>
        Original == null || Original.IsEmpty
            ? 1.0
            : Math.Clamp(Math.Max(Original.Width, Original.Height) / OverlayReferenceLongSide, 0.35, 6.0);

    /// <summary>Scales a base overlay size (in pixels at the reference resolution) to this scan.</summary>
    public int ScaleOverlay(double basePixels) => Math.Max(1, (int)Math.Round(basePixels * OverlayScale));

    private void DrawBoundingBoxes(IEnumerable<CropCandidate> candidates)
    {
        MCvScalar boxColor = CurrentOptions.GetBoundingBoxColorBgr();
        MCvScalar redBadge = new(0, 0, 255); // BGR for Red
        MCvScalar whiteText = new(255, 255, 255);
        int radius = ScaleOverlay(45);
        int boxThickness = ScaleOverlay(12);
        int handleHalf = ScaleOverlay(25);
        int handleBorder = ScaleOverlay(4);
        int crossHalf = ScaleOverlay(15);
        int crossThickness = ScaleOverlay(8);

        foreach (var cand in candidates)
        {
            PointF[] vertices = cand.Rotated.GetVertices();
            for (int j = 0; j < 4; j++)
            {
                CvInvoke.Line(OriginalWithDetected, Point.Round(vertices[j]), Point.Round(vertices[(j + 1) % 4]), boxColor, boxThickness);

                // Draw white square for grab handles
                Rectangle handleRect = new((int)vertices[j].X - handleHalf, (int)vertices[j].Y - handleHalf, handleHalf * 2, handleHalf * 2);
                handleRect.Intersect(new Rectangle(0, 0, OriginalWithDetected.Width, OriginalWithDetected.Height));

                if (handleRect.Width > 0 && handleRect.Height > 0)
                {
                    CvInvoke.Rectangle(OriginalWithDetected, handleRect, whiteText, -1);
                    // Draw border around the handle
                    CvInvoke.Rectangle(OriginalWithDetected, handleRect, boxColor, handleBorder);
                }
            }

            // Find top right corner (max X - Y)
            var sortedVertices = (PointF[])vertices.Clone();
            Array.Sort(sortedVertices, (a, b) => (b.X - b.Y).CompareTo(a.X - a.Y));
            var tr = Point.Round(sortedVertices[0]);

            CvInvoke.Circle(OriginalWithDetected, tr, radius, redBadge, -1);
            CvInvoke.Line(OriginalWithDetected, new Point(tr.X - crossHalf, tr.Y - crossHalf), new Point(tr.X + crossHalf, tr.Y + crossHalf), whiteText, crossThickness);
            CvInvoke.Line(OriginalWithDetected, new Point(tr.X + crossHalf, tr.Y - crossHalf), new Point(tr.X - crossHalf, tr.Y + crossHalf), whiteText, crossThickness);

            // Draw standard rotation handle at top-middle of box
            Rectangle imageBounds = new(0, 0, OriginalWithDetected.Width, OriginalWithDetected.Height);
            PointF[] ordered = PhotoExtractionEngine.OrderBoxPoints(vertices);
            Point topMid = Point.Round(new PointF((ordered[0].X + ordered[1].X) / 2f, (ordered[0].Y + ordered[1].Y) / 2f));
            Point rotHandle = Point.Round(cand.GetRotationHandlePoint((float)(RotationHandleBaseOffset * OverlayScale), imageBounds));

            // Stem connecting top edge to rotation handle
            CvInvoke.Line(OriginalWithDetected, topMid, rotHandle, boxColor, ScaleOverlay(6));

            // Blue rotation handle badge
            MCvScalar blueBadge = new(255, 0, 0); // Blue in BGR
            CvInvoke.Circle(OriginalWithDetected, rotHandle, radius, blueBadge, -1);
            CvInvoke.Circle(OriginalWithDetected, rotHandle, radius, whiteText, ScaleOverlay(3));

            // Draw spinning rotation arrow icon inside the badge
            int arrowRadius = ScaleOverlay(22);
            CvInvoke.Ellipse(OriginalWithDetected, rotHandle, new Size(arrowRadius, arrowRadius), 0, 45, 315, whiteText, ScaleOverlay(5));

            // Arrow head at arc termination (angle 315°)
            double arrowAngleRad = 315.0 * Math.PI / 180.0;
            Point tip = new(
                rotHandle.X + (int)(arrowRadius * Math.Cos(arrowAngleRad)),
                rotHandle.Y + (int)(arrowRadius * Math.Sin(arrowAngleRad)));

            // Tangent direction at 315° is (-sin(315°), cos(315°))
            double tx = -Math.Sin(arrowAngleRad);
            double ty = Math.Cos(arrowAngleRad);
            double nx = -ty;
            double ny = tx;

            double headLen = 12.0 * OverlayScale;
            double headWidth = 8.0 * OverlayScale;

            Point wing1 = new(
                (int)(tip.X - headLen * tx + headWidth * nx),
                (int)(tip.Y - headLen * ty + headWidth * ny));
            Point wing2 = new(
                (int)(tip.X - headLen * tx - headWidth * nx),
                (int)(tip.Y - headLen * ty - headWidth * ny));

            Point[] arrowPoly = [tip, wing1, wing2];
            using var polyVec = new VectorOfPoint(arrowPoly);
            CvInvoke.FillConvexPoly(OriginalWithDetected, polyVec, whiteText);
        }
    }

    public void DetectPhotos()
    {
        ResetState();

        if (Original == null || Original.IsEmpty || Original.Width <= 0 || Original.Height <= 0)
        {
            return;
        }

        if (Original.NumberOfChannels != 3)
        {
            var oldOriginal = Original;
            Original = NormalizeToBgr(oldOriginal);
            oldOriginal.Dispose();
        }

        var preparation = PreparePaddedDetectionImage();
        MCvScalar avgBackgroundColorHsv = preparation.AvgBackgroundColorHsv;
        MCvScalar bgBgr = preparation.BgBgr;
        int pad = preparation.Pad;
        using Mat padded = preparation.Padded;

        // Determine downscale factor for ultra-fast contour detection on high-DPI scans
        int maxDim = Math.Max(padded.Width, padded.Height);
        double scale = 1.0;
        const int targetMaxDim = 1600;

        using Mat scaledDetectionMat = new();
        Mat detectionMat;

        if (maxDim > targetMaxDim)
        {
            scale = (double)targetMaxDim / maxDim;
            int scaledW = (int)Math.Round(padded.Width * scale);
            int scaledH = (int)Math.Round(padded.Height * scale);
            CvInvoke.Resize(padded, scaledDetectionMat, new Size(scaledW, scaledH), 0, 0, Inter.Area);
            detectionMat = scaledDetectionMat;
        }
        else
        {
            detectionMat = padded;
        }

        int scaledPad = (int)Math.Round(pad * scale);
        int originalW = Original.Width;
        int originalH = Original.Height;

        // Precompute HSV and Edge maps once on the detection resolution
        using Mat detHsv = new();
        CvInvoke.CvtColor(detectionMat, detHsv, ColorConversion.Bgr2Hsv);

        using Mat precomputedEdges = ForegroundMaskGenerator.GeneratePrecomputedEdgeMap(
            detectionMat,
            CannyLowThreshold,
            CannyHighThreshold);

        // Multi-pass sensitivity detection:
        // Evaluates balanced search tolerances around base tolerance (both tighter and wider)
        var candidateDetections = new List<CropCandidate>();
        double baseTol = BackgroundTolerance;
        double[] searchTolerances = [
            baseTol,
            baseTol + 4,
            baseTol + 8,
            baseTol + 15,
            Math.Max(5, baseTol - 6),
            Math.Max(2, baseTol - 12)
        ];

        using Mat foreground = new();
        foreach (double tol in searchTolerances)
        {
            ForegroundMaskGenerator.PopulateForegroundMask(
                detectionMat,
                foreground,
                avgBackgroundColorHsv,
                tol,
                CannyLowThreshold,
                CannyHighThreshold,
                precomputedEdges,
                detHsv);

            var passCandidates = CandidateExtractor.ExtractCandidates(
                foreground,
                scaledPad,
                (int)Math.Round(originalW * scale),
                (int)Math.Round(originalH * scale),
                MinAreaFactor,
                MaxAreaFactor,
                detectionMat,
                bgBgr);

            MapCandidatesToOriginalSpace(candidateDetections, passCandidates, scale, originalW, originalH);
        }

        // Automatic Otsu Fallback: If HSV background subtraction failed to find any photos
        // (common with faint vintage prints, light skies, or low-contrast scan lids),
        // run an automatic global luminance Otsu segmentation pass
        if (candidateDetections.Count == 0)
        {
            bool isLightBg = avgBackgroundColorHsv.V2 > 120;
            using Mat otsuForeground = new();
            ForegroundMaskGenerator.PopulateOtsuForegroundMask(
                detectionMat,
                otsuForeground,
                CannyLowThreshold,
                CannyHighThreshold,
                isLightBg,
                precomputedEdges);

            var otsuCandidates = CandidateExtractor.ExtractCandidates(
                otsuForeground,
                scaledPad,
                (int)Math.Round(originalW * scale),
                (int)Math.Round(originalH * scale),
                MinAreaFactor,
                MaxAreaFactor,
                detectionMat,
                bgBgr);

            MapCandidatesToOriginalSpace(candidateDetections, otsuCandidates, scale, originalW, originalH);
        }

        // Composite resolution and overlap filtering
        var acceptedCandidates = CandidateResolutionFilter.FilterCandidates(candidateDetections);

        // Only keep candidates that produced a photo, so AcceptedCandidates[i] always matches DetectedPhotos[i].
        var kept = ExtractAndProcessCandidates(acceptedCandidates, padded, pad);
        AcceptedCandidates = [.. kept.Select(k => k.Candidate)];

        // Draw bounding boxes on OriginalWithDetected
        DrawBoundingBoxes(AcceptedCandidates);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Mat is returned to caller")]
    private (Mat Padded, MCvScalar AvgBackgroundColorHsv, MCvScalar BgBgr, int Pad) PreparePaddedDetectionImage()
    {
        // Sample background color before padding
        using Mat hsv = new();
        CvInvoke.CvtColor(Original, hsv, ColorConversion.Bgr2Hsv);
        MCvScalar avgBackgroundColorHsv = CustomBackgroundColorHsv ?? BackgroundAnalyzer.SampleBackgroundColor(hsv);

        // Convert HSV background color to BGR for border padding
        MCvScalar bgBgr = BackgroundAnalyzer.HsvToBgr(avgBackgroundColorHsv);

        // Pad the full image with a synthetic margin so photos touching or extending to the scan boundary form complete closed contours
        int pad = Math.Max(20, Math.Min(Original.Width, Original.Height) / 50);
        Mat padded = new();
        CvInvoke.CopyMakeBorder(Original, padded, pad, pad, pad, pad, BorderType.Constant, bgBgr);

        // Neutralize scanner bezel / platen border margins in padded detection image
        var (Top, Bottom, Left, Right) = BackgroundAnalyzer.DetectBezelMargins(Original, avgBackgroundColorHsv, BackgroundTolerance);
        if (Top > 0)
        {
            CvInvoke.Rectangle(padded, new Rectangle(0, 0, padded.Width, pad + Top), bgBgr, -1);
        }
        if (Bottom > 0)
        {
            CvInvoke.Rectangle(padded, new Rectangle(0, padded.Height - pad - Bottom, padded.Width, pad + Bottom), bgBgr, -1);
        }
        if (Left > 0)
        {
            CvInvoke.Rectangle(padded, new Rectangle(0, 0, pad + Left, padded.Height), bgBgr, -1);
        }
        if (Right > 0)
        {
            CvInvoke.Rectangle(padded, new Rectangle(padded.Width - pad - Right, 0, pad + Right, padded.Height), bgBgr, -1);
        }

        return (padded, avgBackgroundColorHsv, bgBgr, pad);
    }

    public void SetCustomBackgroundFromPixel(int x, int y)
    {
        CustomBackgroundColorHsv = BackgroundAnalyzer.SamplePixelBackgroundColor(Original, x, y);
    }

    private void RotatePhotoInternal(int index, RotateFlags flags)
    {
        if (index < 0 || index >= DetectedPhotos.Count) return;

        Mat rotated = new();
        CvInvoke.Rotate(DetectedPhotos[index], rotated, flags);
        DetectedPhotos[index].Dispose();
        DetectedPhotos[index] = rotated;

        if (index < RawDetectedPhotos.Count)
        {
            Mat rawRotated = new();
            CvInvoke.Rotate(RawDetectedPhotos[index], rawRotated, flags);
            RawDetectedPhotos[index].Dispose();
            RawDetectedPhotos[index] = rawRotated;
        }

        int delta = flags == RotateFlags.Rotate90Clockwise ? 90 : (flags == RotateFlags.Rotate90CounterClockwise ? 270 : 180);
        int currentRot = PhotoRotations.TryGetValue(index, out int r) ? r : 0;
        PhotoRotations[index] = (currentRot + delta) % 360;
    }

    public void RotatePhoto(int index) => RotatePhotoInternal(index, RotateFlags.Rotate90Clockwise);

    public void RotatePhotoCounterClockwise(int index) => RotatePhotoInternal(index, RotateFlags.Rotate90CounterClockwise);


    public void RedrawBoundingBoxes()
    {
        OriginalWithDetected?.Dispose();
        OriginalWithDetected = Original.Clone();
        if (AcceptedCandidates != null)
        {
            DrawBoundingBoxes(AcceptedCandidates);
        }
    }

    public void UpdateCandidates(IEnumerable<CropCandidate> newCandidates)
    {
        AcceptedCandidates = [.. newCandidates];
        RedrawBoundingBoxes();
    }


    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "ProcessExtractedPhoto takes ownership of the extracted Mat")]
    public void ApplyGrabHandleResize(int candIndex)
    {
        if (candIndex < 0 || candIndex >= AcceptedCandidates.Count || candIndex >= DetectedPhotos.Count) return;

        var cand = AcceptedCandidates[candIndex];

        Mat extracted = PhotoExtractionEngine.ExtractPhotoFromRotatedRect(cand.Rotated, Original);
        if (extracted.IsEmpty)
        {
            // Box dragged too small to extract: keep the previous photo rather than storing an empty one.
            extracted.Dispose();
            return;
        }

        // Re-apply the user's manual 90° rotations so resizing a box doesn't silently undo them.
        int userRotation = PhotoRotations.TryGetValue(candIndex, out int rot) ? rot : 0;
        var (photo, raw, orientation) = ProcessExtractedPhoto(extracted, userRotation: userRotation);
        cand = cand with { OrientationDegrees = orientation };

        DetectedPhotos[candIndex].Dispose();
        DetectedPhotos[candIndex] = photo;

        if (candIndex < RawDetectedPhotos.Count)
        {
            RawDetectedPhotos[candIndex].Dispose();
            RawDetectedPhotos[candIndex] = raw;
        }
        else
        {
            RawDetectedPhotos.Add(raw);
        }

        var vertices = cand.Rotated.GetVertices();
        var pts = new Point[4];
        for (int i = 0; i < 4; i++) pts[i] = Point.Round(vertices[i]);

        using (var v = new VectorOfPoint(pts))
        {
            cand = cand with
            {
                ShapePoints = pts,
                Rect = CvInvoke.BoundingRectangle(v),
                Area = cand.Rotated.Size.Width * cand.Rotated.Size.Height
            };
        }

        var list = new List<CropCandidate>(AcceptedCandidates)
        {
            [candIndex] = cand
        };
        AcceptedCandidates = list;
    }

    public void DeletePhoto(int index)
    {
        if (index < 0 || index >= DetectedPhotos.Count) return;
        DetectedPhotos[index].Dispose();
        DetectedPhotos.RemoveAt(index);

        if (index < RawDetectedPhotos.Count)
        {
            RawDetectedPhotos[index].Dispose();
            RawDetectedPhotos.RemoveAt(index);
        }

        // Shift photo rotations
        PhotoRotations.Remove(index);
        var shiftedRotations = new Dictionary<int, int>();
        foreach (var (k, v) in PhotoRotations)
        {
            shiftedRotations[k > index ? k - 1 : k] = v;
        }
        PhotoRotations.Clear();
        foreach (var (k, v) in shiftedRotations)
        {
            PhotoRotations[k] = v;
        }

        // Always build a new list: AcceptedCandidates instances are handed out to callers (e.g. undo
        // history snapshots), so mutating one in place would silently change their recorded state.
        if (AcceptedCandidates != null && index < AcceptedCandidates.Count)
        {
            var newList = new List<CropCandidate>(AcceptedCandidates);
            newList.RemoveAt(index);
            AcceptedCandidates = newList;
            RedrawBoundingBoxes();
        }
    }

    public void InsertPhoto(int index, Mat photo, CropCandidate? candidate = null)
    {
        ArgumentNullException.ThrowIfNull(photo);

        int targetIndex = Math.Clamp(index, 0, DetectedPhotos.Count);
        DetectedPhotos.Insert(targetIndex, photo);

        int rawTargetIndex = Math.Clamp(index, 0, RawDetectedPhotos.Count);
        RawDetectedPhotos.Insert(rawTargetIndex, photo.Clone());

        var shiftedRotations = new Dictionary<int, int>();
        foreach (var (k, v) in PhotoRotations)
        {
            shiftedRotations[k >= targetIndex ? k + 1 : k] = v;
        }
        PhotoRotations.Clear();
        foreach (var (k, v) in shiftedRotations)
        {
            PhotoRotations[k] = v;
        }

        if (candidate.HasValue && AcceptedCandidates != null)
        {
            int candTargetIndex = Math.Clamp(index, 0, AcceptedCandidates.Count);
            var newList = new List<CropCandidate>(AcceptedCandidates);
            newList.Insert(candTargetIndex, candidate.Value);
            AcceptedCandidates = newList;
            RedrawBoundingBoxes();
        }
    }

    public Rectangle GetRefinedCropRect(int index)
    {
        if (index < 0 || index >= DetectedPhotos.Count) return Rectangle.Empty;
        return EdgeRefinementService.GetRefinedCropRect(DetectedPhotos[index]);
    }

    public void ApplyCropToPhoto(int index, Rectangle rect)
    {
        if (index < 0 || index >= DetectedPhotos.Count) return;

        rect.Intersect(new Rectangle(Point.Empty, DetectedPhotos[index].Size));
        if (rect.Width <= 10 || rect.Height <= 10) return;

        // Move the box on the scan to the refined area too, so the overlay matches the photo and the
        // refinement is saved with the session and survives re-extraction (restore, export).
        if (index < AcceptedCandidates.Count)
        {
            var cand = AcceptedCandidates[index];
            int userRotation = PhotoRotations.TryGetValue(index, out int rot) ? rot : 0;
            int totalRotation = NormalizeRightAngle(cand.OrientationDegrees + userRotation);
            RotatedRect refined = MapPhotoCropToScan(cand.Rotated, totalRotation, DetectedPhotos[index].Size, rect);

            var vertices = refined.GetVertices();
            var pts = vertices.Select(Point.Round).ToArray();
            using var v = new VectorOfPoint(pts);
            var newList = new List<CropCandidate>(AcceptedCandidates)
            {
                [index] = cand with
                {
                    Rotated = refined,
                    ShapePoints = pts,
                    Rect = CvInvoke.BoundingRectangle(v),
                    Area = refined.Size.Width * refined.Size.Height
                }
            };
            AcceptedCandidates = newList;
            RedrawBoundingBoxes();
        }

        // Crop the raw copy with the same rectangle so hold-to-compare keeps showing the same region.
        // Both copies share dimensions (restoration never resizes), but guard in case they diverge.
        if (index < RawDetectedPhotos.Count && RawDetectedPhotos[index].Size == DetectedPhotos[index].Size)
        {
            Mat rawCropped = EdgeRefinementService.ApplyCrop(RawDetectedPhotos[index], rect);
            RawDetectedPhotos[index].Dispose();
            RawDetectedPhotos[index] = rawCropped;
        }

        Mat cropped = EdgeRefinementService.ApplyCrop(DetectedPhotos[index], rect);
        DetectedPhotos[index].Dispose();
        DetectedPhotos[index] = cropped;
    }

    /// <summary>
    /// Maps a crop rectangle in an extracted photo back to a box on the scan.
    /// </summary>
    /// <param name="box">The box the photo was extracted from.</param>
    /// <param name="clockwiseRotation">Total rotation (auto-orientation + user) applied to the extracted photo.</param>
    /// <param name="photoSize">Size of the photo as currently shown, i.e. after that rotation.</param>
    /// <param name="crop">The crop, in the coordinates of the photo as currently shown.</param>
    internal static RotatedRect MapPhotoCropToScan(RotatedRect box, int clockwiseRotation, Size photoSize, Rectangle crop)
    {
        // The extraction warps the box's TL/TR/BL corners to the photo's (0,0)/(W,0)/(0,H) before rotating.
        PointF[] p = PhotoExtractionEngine.OrderBoxPoints(box.GetVertices());
        int rotation = NormalizeRightAngle(clockwiseRotation);
        bool swapped = rotation is 90 or 270;
        float w = swapped ? photoSize.Height : photoSize.Width;
        float h = swapped ? photoSize.Width : photoSize.Height;

        // Undo the rotation: shown-photo point (x, y) -> unrotated extraction point (u, v).
        (float U, float V) Unrotate(float x, float y) => rotation switch
        {
            90 => (y, h - x),
            180 => (w - x, h - y),
            270 => (w - y, x),
            _ => (x, y)
        };

        var (ua, va) = Unrotate(crop.Left, crop.Top);
        var (ub, vb) = Unrotate(crop.Right, crop.Bottom);
        float u0 = Math.Min(ua, ub), u1 = Math.Max(ua, ub);
        float v0 = Math.Min(va, vb), v1 = Math.Max(va, vb);

        PointF ToScan(float u, float v) => new(
            p[0].X + u / w * (p[1].X - p[0].X) + v / h * (p[3].X - p[0].X),
            p[0].Y + u / w * (p[1].Y - p[0].Y) + v / h * (p[3].Y - p[0].Y));

        return CvInvoke.MinAreaRect([ToScan(u0, v0), ToScan(u1, v0), ToScan(u1, v1), ToScan(u0, v1)]);
    }

    public void AddManualCrop(Rectangle rect)
    {
        var (extracted, candidate) = PhotoExtractionEngine.ExtractManualCrop(
            Original,
            rect,
            CustomBackgroundColorHsv,
            BackgroundTolerance,
            CannyLowThreshold,
            CannyHighThreshold);

        if (extracted.IsEmpty || !candidate.HasValue)
        {
            extracted.Dispose();
            return;
        }

        // Same pipeline as detected photos, so a manual crop looks the same now as after a restart.
        var (photo, raw, orientation) = ProcessExtractedPhoto(extracted);
        DetectedPhotos.Add(photo);
        RawDetectedPhotos.Add(raw);
        AcceptedCandidates = [.. AcceptedCandidates ?? [], candidate.Value with { OrientationDegrees = orientation }];
        RedrawBoundingBoxes();
    }

    /// <returns>The files written; pass them back as <paramref name="previousExports"/> on the next export.</returns>
    public IReadOnlyList<ExportedFile> SaveDetectedPhotos(
        string? customOutputFolder = null,
        string format = Common.AppConstants.DefaultImageFormat,
        int jpegQuality = 100,
        string fileNamePattern = FileNameTemplateHelper.DefaultPattern,
        PhotoExportMetadata? metadata = null,
        Action<int, int>? progressCallback = null,
        bool cleanOldExports = false,
        IEnumerable<ExportedFile>? previousExports = null)
    {
        return PhotoExporter.SavePhotos(DetectedPhotos, OriginalFilePath, customOutputFolder, format, jpegQuality, fileNamePattern, metadata, progressCallback, cleanOldExports, previousExports);
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
                PointF fullCenter = new((float)(cand.Rotated.Center.X * invScale), (float)(cand.Rotated.Center.Y * invScale));
                SizeF fullSize = new((float)(cand.Rotated.Size.Width * invScale), (float)(cand.Rotated.Size.Height * invScale));
                RotatedRect fullRr = new(fullCenter, fullSize, cand.Rotated.Angle);

                PointF[] fullVerts = fullRr.GetVertices();
                Point[] fullPoints = new Point[4];
                for (int p = 0; p < 4; p++)
                {
                    fullPoints[p] = new Point(
                        Math.Clamp((int)Math.Round(fullVerts[p].X), 0, originalW - 1),
                        Math.Clamp((int)Math.Round(fullVerts[p].Y), 0, originalH - 1)
                    );
                }

                using VectorOfPoint fullShape = new(fullPoints);
                double fullArea = (double)fullSize.Width * fullSize.Height;
                double score = fullArea * Math.Pow(cand.Rectangularity, 3) * Math.Pow(cand.Convexity, 2);

                destination.Add(new CropCandidate(
                    fullPoints,
                    CvInvoke.BoundingRectangle(fullShape),
                    score,
                    fullRr,
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
