using System.Collections.ObjectModel;
using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using PhotoCropper.Detection;
using PhotoCropper.Export;
using PhotoCropper.Extraction;
using PhotoCropper.Models;

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

        // Pad the image with a synthetic margin so photos touching or extending to the scan boundary form complete closed contours
        int pad = Math.Max(20, Math.Min(Original.Width, Original.Height) / 50);
        using Mat padded = new();
        CvInvoke.CopyMakeBorder(Original, padded, pad, pad, pad, pad, BorderType.Constant, bgBgr);

        // Multi-pass sensitivity detection:
        // Evaluates fine-grained search tolerances around base tolerance
        var candidateDetections = new List<CropCandidate>();
        double baseTol = BackgroundTolerance;
        double[] searchTolerances = [
            baseTol,
            baseTol + 3,
            baseTol + 6,
            baseTol + 10,
            baseTol + 15,
            baseTol + 22,
            baseTol + 32,
            Math.Max(5, baseTol - 5),
            Math.Max(5, baseTol - 10)
        ];

        using Mat foreground = new();
        foreach (double tol in searchTolerances)
        {
            ForegroundMaskGenerator.PopulateForegroundMask(padded, foreground, avgBackgroundColorHsv, tol, CannyLowThreshold, CannyHighThreshold);
            
            var passCandidates = CandidateExtractor.ExtractCandidates(
                foreground, 
                pad, 
                Original.Width, 
                Original.Height, 
                MinAreaFactor, 
                MaxAreaFactor);

            candidateDetections.AddRange(passCandidates);
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

        // Parallel extraction: Rotate and crop each photo on different CPU cores using the padded source
        Mat?[] results = new Mat[acceptedCandidates.Count];
        Parallel.For(0, acceptedCandidates.Count, i =>
        {
            using VectorOfPoint poly = new(acceptedCandidates[i].ShapePoints);
            results[i] = PhotoExtractionEngine.ExtractPhotoFromContour(poly, Original, padded, pad);
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

    public void SaveDetectedPhotos(string? customOutputFolder = null, string format = "JPEG", int jpegQuality = 90)
    {
        PhotoExporter.SavePhotos(DetectedPhotos, OriginalFilePath, customOutputFolder, format, jpegQuality);
    }
}
