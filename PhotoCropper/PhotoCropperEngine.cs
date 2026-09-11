using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using PhotoCropper.Detection;
using PhotoCropper.Export;
using PhotoCropper.Extraction;
using PhotoCropper.Models;
using System.Collections.ObjectModel;
using System.Drawing;

namespace PhotoCropper;

public class PhotoCropperEngine : IDisposable
{
    private bool disposedValue;

    public string OriginalFilePath { get; }

    // Configurable Detection Parameters
    public double BackgroundTolerance { get; set; } = 30;
    public double MinAreaFactor { get; set; } = 0.01; // 1% of scan
    public double MaxAreaFactor { get; set; } = 0.90; // 90% of scan
    public double CannyLowThreshold { get; set; } = 20;
    public double CannyHighThreshold { get; set; } = 50;
    public MCvScalar? CustomBackgroundColorHsv { get; set; }
    public bool AutoOrientPhotos { get; set; }

    public Mat Original { get; set; }
    public Mat OriginalWithDetected { get; set; }
    public Collection<Mat> DetectedPhotos { get; } = [];

    public PhotoCropperEngine(string originalFilePath)
    {
        OriginalFilePath = originalFilePath;
        Original = CvInvoke.Imread(originalFilePath, ImreadModes.AnyColor);
        OriginalWithDetected = Original.Clone();
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
    }

    private void ResetState()
    {
        foreach (var photo in DetectedPhotos) photo.Dispose();
        DetectedPhotos.Clear();

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
            }
            disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public void DetectPhotos()
    {
        ResetState();

        // Sample background color before padding
        using Mat hsv = new();
        CvInvoke.CvtColor(Original, hsv, ColorConversion.Bgr2Hsv);
        MCvScalar avgBackgroundColorHsv = CustomBackgroundColorHsv ?? BackgroundAnalyzer.SampleBackgroundColor(hsv);

        // Convert HSV background color to BGR for border padding
        MCvScalar bgBgr = BackgroundAnalyzer.HsvToBgr(avgBackgroundColorHsv);

        // Pad the full image with a synthetic margin so photos touching or extending to the scan boundary form complete closed contours
        int pad = Math.Max(20, Math.Min(Original.Width, Original.Height) / 50);
        using Mat padded = new();
        CvInvoke.CopyMakeBorder(Original, padded, pad, pad, pad, pad, BorderType.Constant, bgBgr);

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
        // Evaluates fine-grained search tolerances around base tolerance
        var candidateDetections = new List<CropCandidate>();
        double baseTol = BackgroundTolerance;
        double[] searchTolerances = [
            baseTol,
            baseTol + 4,
            baseTol + 8,
            baseTol + 15,
            baseTol + 25,
            Math.Max(5, baseTol - 6)
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
                MaxAreaFactor);

            // If downscaled, map candidate coordinates back to original full resolution space
            if (scale < 0.999)
            {
                double invScale = 1.0 / scale;
                foreach (var cand in passCandidates)
                {
                    Point[] fullPoints = new Point[cand.ShapePoints.Length];
                    for (int p = 0; p < cand.ShapePoints.Length; p++)
                    {
                        fullPoints[p] = new Point(
                            Math.Clamp((int)Math.Round(cand.ShapePoints[p].X * invScale), 0, originalW - 1),
                            Math.Clamp((int)Math.Round(cand.ShapePoints[p].Y * invScale), 0, originalH - 1)
                        );
                    }

                    using VectorOfPoint fullShape = new(fullPoints);
                    RotatedRect fullRr = PhotoExtractionEngine.RegularizeNearRightAngles(CvInvoke.MinAreaRect(fullShape));
                    double fullArea = CvInvoke.ContourArea(fullShape);
                    double rrArea = Math.Max(1.0, (double)fullRr.Size.Width * fullRr.Size.Height);
                    double rectScore = Math.Clamp(fullArea / rrArea, 0.0, 1.0);
                    double quality = Math.Pow(rectScore, 3) * Math.Pow(cand.Convexity, 2);
                    double score = fullArea * quality;

                    candidateDetections.Add(new CropCandidate(
                        fullPoints,
                        CvInvoke.BoundingRectangle(fullShape),
                        score,
                        fullRr,
                        fullArea,
                        rectScore,
                        cand.Convexity));
                }
            }
            else
            {
                candidateDetections.AddRange(passCandidates);
            }
        }

        // Composite resolution and overlap filtering
        var acceptedCandidates = CandidateResolutionFilter.FilterCandidates(candidateDetections);

        // Draw bounding boxes on OriginalWithDetected
        foreach (var cand in acceptedCandidates)
        {
            PointF[] vertices = cand.Rotated.GetVertices();
            for (int j = 0; j < 4; j++)
            {
                CvInvoke.Line(OriginalWithDetected, Point.Round(vertices[j]), Point.Round(vertices[(j + 1) % 4]), new MCvScalar(0, 0, 255), 12);
            }
        }

        // Parallel extraction: Rotate and crop each photo on different CPU cores using the padded source at FULL scan resolution
        Mat?[] results = new Mat[acceptedCandidates.Count];
        Parallel.For(0, acceptedCandidates.Count, i =>
        {
            using VectorOfPoint poly = new(acceptedCandidates[i].ShapePoints);
            Mat extracted = PhotoExtractionEngine.ExtractPhotoFromContour(poly, Original, padded, pad);
            if (AutoOrientPhotos && !extracted.IsEmpty)
            {
                Mat oriented = AutoOrientationService.OrientPhoto(extracted);
                if (!ReferenceEquals(oriented, extracted))
                {
                    extracted.Dispose();
                    extracted = oriented;
                }
            }
            results[i] = extracted;
        });

        foreach (var mat in results)
        {
            if (mat != null && !mat.IsEmpty)
            {
                DetectedPhotos.Add(mat);
            }
        }
    }

    public void SetCustomBackgroundFromPixel(int x, int y)
    {
        CustomBackgroundColorHsv = BackgroundAnalyzer.SamplePixelBackgroundColor(Original, x, y);
    }

    public void RotatePhoto(int index)
    {
        if (index < 0 || index >= DetectedPhotos.Count) return;

        Mat rotated = new();
        CvInvoke.Rotate(DetectedPhotos[index], rotated, RotateFlags.Rotate90Clockwise);
        DetectedPhotos[index].Dispose();
        DetectedPhotos[index] = rotated;
    }

    public void DeletePhoto(int index)
    {
        if (index < 0 || index >= DetectedPhotos.Count) return;
        DetectedPhotos[index].Dispose();
        DetectedPhotos.RemoveAt(index);
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
        Mat extracted = PhotoExtractionEngine.ExtractManualCrop(
            Original,
            rect,
            CustomBackgroundColorHsv,
            BackgroundTolerance,
            CannyLowThreshold,
            CannyHighThreshold);

        if (!extracted.IsEmpty)
        {
            DetectedPhotos.Add(extracted);
        }
        else
        {
            extracted.Dispose();
        }
    }

    public void SaveDetectedPhotos(string? customOutputFolder = null, string format = "JPEG", int jpegQuality = 90, Action<int, int>? progressCallback = null)
    {
        PhotoExporter.SavePhotos(DetectedPhotos, OriginalFilePath, customOutputFolder, format, jpegQuality, progressCallback);
    }
}
