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
    public bool RestoreVintageColors { get; set; } = true;
    public bool RemoveDustAndScratches { get; set; } = true;
    public string BoundingBoxColor { get; set; } = Common.AppConstants.DefaultBoundingBoxColor;

    public Mat Original { get; set; }
    public Mat OriginalWithDetected { get; set; }
    public Collection<Mat> DetectedPhotos { get; } = [];
    public Collection<Mat> RawDetectedPhotos { get; } = [];
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

        var candidates = new List<CropCandidate>();
        foreach (var crop in savedCrops)
        {
            var center = new PointF(crop.CenterX, crop.CenterY);
            var size = new SizeF(crop.Width, crop.Height);
            var rr = new RotatedRect(center, size, crop.Angle);
            candidates.Add(new CropCandidate { Rotated = rr, Area = crop.Width * crop.Height, ShapePoints = [], Rect = new Rectangle(0, 0, (int)crop.Width, (int)crop.Height) });
        }

        AcceptedCandidates = candidates;

        DrawBoundingBoxes(AcceptedCandidates);

        ExtractAndProcessCandidates(candidates);
    }


    private void ExtractAndProcessCandidates(System.Collections.Generic.IReadOnlyList<PhotoCropper.Core.Models.CropCandidate> candidates, Mat? padded = null, int pad = 0)
    {
        Mat?[] results = new Mat[candidates.Count];
        Mat?[] rawResults = new Mat[candidates.Count];

        System.Threading.Tasks.Parallel.For(0, candidates.Count, i =>
        {
            Mat extracted = (padded != null && !padded.IsEmpty && pad > 0)
                ? PhotoCropper.Core.Extraction.PhotoExtractionEngine.ExtractPhotoFromRotatedRect(candidates[i].Rotated, Original, padded, pad)
                : PhotoCropper.Core.Extraction.PhotoExtractionEngine.ExtractPhotoFromRotatedRect(candidates[i].Rotated, Original);

            if (AutoOrientPhotos && !extracted.IsEmpty)
            {
                Mat oriented = PhotoCropper.Core.Extraction.AutoOrientationService.OrientPhoto(extracted);
                if (!ReferenceEquals(oriented, extracted))
                {
                    extracted.Dispose();
                    extracted = oriented;
                }
            }

            rawResults[i] = extracted.Clone();

            if (RestoreVintageColors && !extracted.IsEmpty)
            {
                Mat restored = PhotoCropper.Core.Extraction.PhotoRestorationService.RestoreColors(extracted, removeDust: RemoveDustAndScratches);
                if (!ReferenceEquals(restored, extracted))
                {
                    extracted.Dispose();
                    extracted = restored;
                }
            }
            results[i] = extracted;
        });

        for (int i = 0; i < results.Length; i++)
        {
            var mat = results[i];
            var raw = rawResults[i];
            if (mat != null && !mat.IsEmpty && raw != null && !raw.IsEmpty)
            {
                DetectedPhotos.Add(mat);
                RawDetectedPhotos.Add(raw);
            }
            else
            {
                mat?.Dispose();
                raw?.Dispose();
            }
        }
    }


    private void DrawBoundingBoxes(IEnumerable<CropCandidate> candidates)
    {
        MCvScalar boxColor = CurrentOptions.GetBoundingBoxColorBgr();
        MCvScalar redBadge = new(0, 0, 255); // BGR for Red
        MCvScalar whiteText = new(255, 255, 255);
        int radius = 45;

        foreach (var cand in candidates)
        {
            PointF[] vertices = cand.Rotated.GetVertices();
            for (int j = 0; j < 4; j++)
            {
                CvInvoke.Line(OriginalWithDetected, Point.Round(vertices[j]), Point.Round(vertices[(j + 1) % 4]), boxColor, 12);

                // Draw white square for grab handles
                Rectangle handleRect = new((int)vertices[j].X - 25, (int)vertices[j].Y - 25, 50, 50);
                handleRect.Intersect(new Rectangle(0, 0, OriginalWithDetected.Width, OriginalWithDetected.Height));

                if (handleRect.Width > 0 && handleRect.Height > 0)
                {
                    CvInvoke.Rectangle(OriginalWithDetected, handleRect, whiteText, -1);
                    // Draw border around the handle
                    CvInvoke.Rectangle(OriginalWithDetected, handleRect, boxColor, 4);
                }
            }

            // Find top right corner (max X - Y)
            var sortedVertices = (PointF[])vertices.Clone();
            Array.Sort(sortedVertices, (a, b) => (b.X - b.Y).CompareTo(a.X - a.Y));
            var tr = Point.Round(sortedVertices[0]);

            CvInvoke.Circle(OriginalWithDetected, tr, radius, redBadge, -1);
            CvInvoke.Line(OriginalWithDetected, new Point(tr.X - 15, tr.Y - 15), new Point(tr.X + 15, tr.Y + 15), whiteText, 8);
            CvInvoke.Line(OriginalWithDetected, new Point(tr.X + 15, tr.Y - 15), new Point(tr.X - 15, tr.Y + 15), whiteText, 8);

            // Draw standard rotation handle at top-middle of box
            PointF[] ordered = PhotoExtractionEngine.OrderBoxPoints(vertices);
            Point topMid = Point.Round(new PointF((ordered[0].X + ordered[1].X) / 2f, (ordered[0].Y + ordered[1].Y) / 2f));
            Point rotHandle = Point.Round(cand.GetRotationHandlePoint(60f));

            // Stem connecting top edge to rotation handle
            CvInvoke.Line(OriginalWithDetected, topMid, rotHandle, boxColor, 6);

            // Blue rotation handle badge
            MCvScalar blueBadge = new(255, 0, 0); // Blue in BGR
            CvInvoke.Circle(OriginalWithDetected, rotHandle, radius, blueBadge, -1);
            CvInvoke.Circle(OriginalWithDetected, rotHandle, radius, whiteText, 3);
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
        AcceptedCandidates = acceptedCandidates;

        // Draw bounding boxes on OriginalWithDetected
        DrawBoundingBoxes(acceptedCandidates);

        ExtractAndProcessCandidates(acceptedCandidates, padded, pad);
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


    public void ApplyGrabHandleResize(int candIndex)
    {
        var cand = AcceptedCandidates[candIndex];

        Mat extracted = PhotoExtractionEngine.ExtractPhotoFromRotatedRect(cand.Rotated, Original);

        if (AutoOrientPhotos && !extracted.IsEmpty)
        {
            Mat oriented = AutoOrientationService.OrientPhoto(extracted);
            if (!ReferenceEquals(oriented, extracted))
            {
                extracted.Dispose();
                extracted = oriented;
            }
        }

        if (RestoreVintageColors && !extracted.IsEmpty)
        {
            Mat restored = PhotoRestorationService.RestoreColors(extracted, removeDust: RemoveDustAndScratches);
            if (!ReferenceEquals(restored, extracted))
            {
                extracted.Dispose();
                extracted = restored;
            }
        }

        DetectedPhotos[candIndex].Dispose();
        DetectedPhotos[candIndex] = extracted;

        if (candIndex < RawDetectedPhotos.Count)
        {
            RawDetectedPhotos[candIndex].Dispose();
            RawDetectedPhotos[candIndex] = extracted.Clone();
        }
        else
        {
            RawDetectedPhotos.Add(extracted.Clone());
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

        if (AcceptedCandidates is List<CropCandidate> list && index < list.Count)
        {
            list.RemoveAt(index);
            RedrawBoundingBoxes();
        }
        else if (AcceptedCandidates != null && index < AcceptedCandidates.Count)
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

        Mat cropped = EdgeRefinementService.ApplyCrop(DetectedPhotos[index], rect);
        DetectedPhotos[index].Dispose();
        DetectedPhotos[index] = cropped;
    }

    public void AddManualCrop(Rectangle rect)
    {
        var (Extracted, Candidate) = PhotoExtractionEngine.ExtractManualCrop(
            Original,
            rect,
            CustomBackgroundColorHsv,
            BackgroundTolerance,
            CannyLowThreshold,
            CannyHighThreshold);

        if (!Extracted.IsEmpty)
        {
            DetectedPhotos.Add(Extracted);
            RawDetectedPhotos.Add(Extracted.Clone());
            if (Candidate.HasValue)
            {
                var cand = Candidate.Value;
                var newList = new List<CropCandidate>(AcceptedCandidates ?? [])
                {
                    cand
                };
                AcceptedCandidates = newList;
                RedrawBoundingBoxes();
            }
        }
        else
        {
            Extracted.Dispose();
        }
    }

    public void SaveDetectedPhotos(
        string? customOutputFolder = null,
        string format = Common.AppConstants.DefaultImageFormat,
        int jpegQuality = 100,
        string fileNamePattern = FileNameTemplateHelper.DefaultPattern,
        PhotoExportMetadata? metadata = null,
        Action<int, int>? progressCallback = null,
        bool cleanOldExports = false)
    {
        PhotoExporter.SavePhotos(DetectedPhotos, OriginalFilePath, customOutputFolder, format, jpegQuality, fileNamePattern, metadata, progressCallback, cleanOldExports);
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
