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

    public double BackgroundTolerance { get; set; } = 30;

    public PhotoCropper(string originalFilePath)
    {
        this.originalFilePath = originalFilePath;
        Original = CvInvoke.Imread(originalFilePath, ImreadModes.ColorRgb);

        // Minimum 1% of scan, maximum 90%
        MIN_AREA_THRESHOLD = (Original.Width * Original.Height) * 0.01;
        MAX_AREA_THRESHOLD = (Original.Width * Original.Height) * 0.90;

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

        using Mat hsv = new();
        CvInvoke.CvtColor(Original, hsv, ColorConversion.Bgr2Hsv);

        // 1. Sample Background from 4 Corners
        int s = 15;
        var samples = new List<MCvScalar> {
            CvInvoke.Mean(new Mat(hsv, new Rectangle(5, 5, s, s))),
            CvInvoke.Mean(new Mat(hsv, new Rectangle(Original.Width - s - 5, 5, s, s))),
            CvInvoke.Mean(new Mat(hsv, new Rectangle(5, Original.Height - s - 5, s, s))),
            CvInvoke.Mean(new Mat(hsv, new Rectangle(Original.Width - s - 5, Original.Height - s - 5, s, s)))
        };

        double hAvg = samples.Average(x => x.V0);
        double sAvg = samples.Average(x => x.V1);
        double vAvg = samples.Average(x => x.V2);

        // 2. Create Background Mask using Slider Sensitivity
        double hTol = BackgroundTolerance * 0.4;
        double sTol = BackgroundTolerance;
        double vTol = BackgroundTolerance;
        MCvScalar lower = new MCvScalar(Math.Max(0, hAvg - hTol), Math.Max(0, sAvg - sTol), Math.Max(0, vAvg - vTol));
        MCvScalar upper = new MCvScalar(Math.Min(180, hAvg + hTol), Math.Min(255, sAvg + sTol), Math.Min(255, vAvg + vTol));

        using Mat backgroundMask = new();
        CvInvoke.InRange(hsv, new ScalarArray(lower), new ScalarArray(upper), backgroundMask);

        // 3. Edge-Bridge (Canny) to catch subtle photo margins
        using Mat gray = new();
        CvInvoke.CvtColor(Original, gray, ColorConversion.Bgr2Gray);
        using Mat edges = new();
        CvInvoke.GaussianBlur(gray, edges, new Size(5, 5), 1.5);
        CvInvoke.Canny(edges, edges, 20, 50);

        // 4. Combine: Not Background OR Edges
        using Mat foreground = new();
        CvInvoke.BitwiseNot(backgroundMask, foreground);
        CvInvoke.BitwiseOr(foreground, edges, foreground);

        // 5. Morphological Cleanup
        using Mat kernel = CvInvoke.GetStructuringElement(MorphShapes.Rectangle, new Size(11, 11), new Point(-1, -1));
        CvInvoke.MorphologyEx(foreground, foreground, MorphOp.Close, kernel, new Point(-1, -1), 3, BorderType.Default, new MCvScalar());
        CvInvoke.MorphologyEx(foreground, foreground, MorphOp.Open, kernel, new Point(-1, -1), 1, BorderType.Default, new MCvScalar());

        // 6. Find Contours
        using VectorOfVectorOfPoint contours = new();
        CvInvoke.FindContours(foreground, contours, null, RetrType.External, ChainApproxMethod.ChainApproxSimple);

        var candidates = new List<(Rectangle Rect, double Area, VectorOfPoint Contour)>();
        for (int i = 0; i < contours.Size; i++)
        {
            double area = CvInvoke.ContourArea(contours[i]);
            if (area > MIN_AREA_THRESHOLD && area < MAX_AREA_THRESHOLD)
            {
                candidates.Add((CvInvoke.BoundingRectangle(contours[i]), area, new VectorOfPoint(contours[i].ToArray())));
            }
        }

        // 7. Non-Maximum Suppression
        var sorted = candidates.OrderByDescending(c => c.Area).ToList();
        var accepted = new List<Rectangle>();

        foreach (var cand in sorted)
        {
            Point center = new Point(cand.Rect.X + cand.Rect.Width / 2, cand.Rect.Y + cand.Rect.Height / 2);
            if (accepted.Any(r => r.Contains(center))) continue;

            accepted.Add(cand.Rect);
            CvInvoke.Rectangle(OriginalWithDetected, cand.Rect, new MCvScalar(0, 0, 255), 12);
            
            var extracted = ExtractPhotoFromContour(cand.Contour);
            if (extracted != null && !extracted.IsEmpty)
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

        return new Mat(rotatedImage, boundingRect).Clone();
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
