using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using System.Drawing;
using Emgu.CV.Util;
using System.Linq;

namespace PhotoCropper;

public class PhotoCropper : IDisposable
{
    private double MIN_AREA_THRESHOLD = 0;
    private double MAX_AREA_THRESHOLD = 0;
    private readonly string originalFilePath;
    private bool disposedValue;

    public PhotoCropper(string originalFilePath)
    {
        this.originalFilePath = originalFilePath;
        Original = CvInvoke.Imread(originalFilePath, ImreadModes.ColorRgb);

        // Minimum 0.5% of scan, maximum 80% (to avoid picking up the whole page/scanner bed)
        MIN_AREA_THRESHOLD = (Original.Width * Original.Height) * 0.005;
        MAX_AREA_THRESHOLD = (Original.Width * Original.Height) * 0.80;

        OriginalWithDetected = Original.Clone();
    }

    public Mat Original { get; set; }
    public Mat OriginalWithDetected { get; set; }
    public List<Mat> DetectedPhotos { get; set; } = [];

    public void DetectPhotos()
    {
        foreach (var photo in DetectedPhotos) photo.Dispose();
        DetectedPhotos.Clear();
        
        OriginalWithDetected?.Dispose();
        OriginalWithDetected = Original.Clone();

        using Mat gray = new();
        CvInvoke.CvtColor(Original, gray, ColorConversion.Bgr2Gray);
        
        // 1. Moderate contrast enhancement
        CvInvoke.Normalize(gray, gray, 0, 255, NormType.MinMax);

        // 2. Edge Detection
        using Mat edges = new();
        CvInvoke.GaussianBlur(gray, edges, new Size(5, 5), 1.5);
        CvInvoke.Canny(edges, edges, 30, 90);

        // 3. Adaptive Thresholding (detects subtle brightness changes)
        using Mat thresh = new();
        CvInvoke.AdaptiveThreshold(gray, thresh, 255, AdaptiveThresholdType.GaussianC, ThresholdType.BinaryInv, 15, 4);

        // 4. Combine
        using Mat combined = new();
        CvInvoke.BitwiseOr(edges, thresh, combined);

        // 5. Solidify shapes
        using Mat kernel = CvInvoke.GetStructuringElement(Emgu.CV.CvEnum.MorphShapes.Rectangle, new Size(7, 7), new Point(-1, -1));
        CvInvoke.MorphologyEx(combined, combined, MorphOp.Close, kernel, new Point(-1, -1), 2, BorderType.Default, new MCvScalar());

        using VectorOfVectorOfPoint contours = new();
        CvInvoke.FindContours(combined, contours, null, RetrType.List, ChainApproxMethod.ChainApproxSimple);

        var candidates = new List<(Rectangle Rect, double Area, VectorOfPoint Contour)>();

        for (int i = 0; i < contours.Size; i++)
        {
            double area = CvInvoke.ContourArea(contours[i]);
            // If it's within our 0.5% - 80% range
            if (area > MIN_AREA_THRESHOLD && area < MAX_AREA_THRESHOLD)
            {
                Rectangle rect = CvInvoke.BoundingRectangle(contours[i]);
                candidates.Add((rect, area, new VectorOfPoint(contours[i].ToArray())));
            }
        }

        // Sort by area DESCENDING (largest first)
        var sortedCandidates = candidates.OrderByDescending(c => c.Area).ToList();
        var acceptedRects = new List<Rectangle>();

        foreach (var candidate in sortedCandidates)
        {
            // If the center of this candidate is inside an already accepted (larger) rectangle, it's a sub-shape (skip it)
            Point center = new Point(candidate.Rect.X + candidate.Rect.Width / 2, candidate.Rect.Y + candidate.Rect.Height / 2);
            
            if (acceptedRects.Any(r => r.Contains(center))) continue;

            // Also check for significant overlap
            if (acceptedRects.Any(r => {
                Rectangle intersect = Rectangle.Intersect(r, candidate.Rect);
                return intersect.Width * intersect.Height > candidate.Area * 0.5; // More than 50% overlap
            })) continue;

            acceptedRects.Add(candidate.Rect);
            
            CvInvoke.Rectangle(OriginalWithDetected, candidate.Rect, new MCvScalar(0, 0, 255), 12);
            
            var extracted = ExtractPhotoFromContour(candidate.Contour);
            if (!extracted.IsEmpty)
            {
                DetectedPhotos.Add(extracted);
            }
        }
    }

    private Mat ExtractPhotoFromContour(VectorOfPoint contour)
    {
        RotatedRect minAreaRect = CvInvoke.MinAreaRect(contour);
        double angle = minAreaRect.Angle;
        if (Math.Abs(angle) > 45) angle -= 90;

        using Mat rotationMatrix = new();
        CvInvoke.GetRotationMatrix2D(minAreaRect.Center, angle, 1.0, rotationMatrix);

        using Mat rotatedImage = new();
        CvInvoke.WarpAffine(Original, rotatedImage, rotationMatrix, Original.Size, Inter.Linear, Warp.Default, BorderType.Constant, new MCvScalar(255, 255, 255));

        Rectangle boundingRect = minAreaRect.MinAreaRect();
        boundingRect.Intersect(new Rectangle(Point.Empty, rotatedImage.Size));
        
        if (boundingRect.Width <= 10 || boundingRect.Height <= 10) return new Mat();

        // Initial crop
        using Mat cropped = new Mat(rotatedImage, boundingRect);
        
        // Refinement: Try to find a tighter crop inside to remove unnecessary white borders
        using Mat grayCropped = new();
        CvInvoke.CvtColor(cropped, grayCropped, ColorConversion.Bgr2Gray);
        using Mat binaryCropped = new();
        CvInvoke.Threshold(grayCropped, binaryCropped, 0, 255, ThresholdType.BinaryInv | ThresholdType.Otsu);

        using VectorOfVectorOfPoint subContours = new();
        CvInvoke.FindContours(binaryCropped, subContours, null, RetrType.External, ChainApproxMethod.ChainApproxSimple);

        Rectangle tightRect = Rectangle.Empty;
        double maxArea = 0;
        for (int j = 0; j < subContours.Size; j++)
        {
            double area = CvInvoke.ContourArea(subContours[j]);
            if (area > maxArea)
            {
                maxArea = area;
                tightRect = CvInvoke.BoundingRectangle(subContours[j]);
            }
        }

        // If the tight crop is reasonable (not just a tiny speck), use it
        if (tightRect.IsEmpty || tightRect.Width < cropped.Width * 0.6) 
            return cropped.Clone();

        return new Mat(cropped, tightRect).Clone();
    }

    public void SaveDetectedPhotos()
    {
        string? directory = Path.GetDirectoryName(originalFilePath);
        if (string.IsNullOrEmpty(directory)) return;

        string outputFolder = Path.Combine(directory, "cropped");
        Directory.CreateDirectory(outputFolder);

        string baseFileName = Path.GetFileNameWithoutExtension(originalFilePath);

        for (int i = 0; i < DetectedPhotos.Count; i++)
        {
            if (DetectedPhotos[i].IsEmpty) continue;
            string fileName = Path.Combine(outputFolder, $"{baseFileName}_{i + 1}.jpg");
            DetectedPhotos[i].Save(fileName);
        }
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
}
