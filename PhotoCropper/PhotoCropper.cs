using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using System.Drawing;
using Emgu.CV.Util;

namespace PhotoCropper;

public class PhotoCropper : IDisposable
{
    private readonly double MIN_AREA_THRESHOLD = 0;
    private readonly double MAX_AREA_THRESHOLD = 0;
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
        ResetState();

        using Mat hsv = new();
        CvInvoke.CvtColor(Original, hsv, ColorConversion.Bgr2Hsv);

        MCvScalar avgBackgroundColor = SampleBackgroundColor(hsv);
        using Mat backgroundMask = CreateBackgroundMask(hsv, avgBackgroundColor);
        using Mat edges = PerformEdgeDetection();

        using Mat foreground = CreateForegroundMap(backgroundMask, edges);
        RefineForegroundMap(foreground);

        ProcessContours(foreground);
    }

    private void ResetState()
    {
        foreach (var photo in DetectedPhotos) photo.Dispose();
        DetectedPhotos.Clear();

        OriginalWithDetected?.Dispose();
        OriginalWithDetected = Original.Clone();
    }

    private MCvScalar SampleBackgroundColor(Mat hsv)
    {
        int s = 15; // sample size
        var samples = new List<MCvScalar> {
            CvInvoke.Mean(new Mat(hsv, new Rectangle(5, 5, s, s))),
            CvInvoke.Mean(new Mat(hsv, new Rectangle(Original.Width - s - 5, 5, s, s))),
            CvInvoke.Mean(new Mat(hsv, new Rectangle(5, Original.Height - s - 5, s, s))),
            CvInvoke.Mean(new Mat(hsv, new Rectangle(Original.Width - s - 5, Original.Height - s - 5, s, s)))
        };

        return new MCvScalar(
            samples.Average(x => x.V0),
            samples.Average(x => x.V1),
            samples.Average(x => x.V2)
        );
    }

    private Mat CreateBackgroundMask(Mat hsv, MCvScalar avgColor)
    {
        double hTol = BackgroundTolerance * 0.4;
        double sTol = BackgroundTolerance;
        double vTol = BackgroundTolerance;

        MCvScalar lower = new(Math.Max(0, avgColor.V0 - hTol), Math.Max(0, avgColor.V1 - sTol), Math.Max(0, avgColor.V2 - vTol));
        MCvScalar upper = new(Math.Min(180, avgColor.V0 + hTol), Math.Min(255, avgColor.V1 + sTol), Math.Min(255, avgColor.V2 + vTol));

        Mat mask = new();
        CvInvoke.InRange(hsv, new ScalarArray(lower), new ScalarArray(upper), mask);
        return mask;
    }

    private Mat PerformEdgeDetection()
    {
        using Mat gray = new();
        CvInvoke.CvtColor(Original, gray, ColorConversion.Bgr2Gray);
        Mat edges = new();
        CvInvoke.GaussianBlur(gray, edges, new Size(5, 5), 1.5);
        CvInvoke.Canny(edges, edges, 20, 50);
        return edges;
    }

    private static Mat CreateForegroundMap(Mat backgroundMask, Mat edges)
    {
        Mat foreground = new();
        CvInvoke.BitwiseNot(backgroundMask, foreground);
        CvInvoke.BitwiseOr(foreground, edges, foreground);
        return foreground;
    }

    private static void RefineForegroundMap(Mat foreground)
    {
        using Mat kernel = CvInvoke.GetStructuringElement(MorphShapes.Rectangle, new Size(5, 5), new Point(-1, -1));
        CvInvoke.MorphologyEx(foreground, foreground, MorphOp.Close, kernel, new Point(-1, -1), 2, BorderType.Default, new MCvScalar());
        CvInvoke.MorphologyEx(foreground, foreground, MorphOp.Erode, kernel, new Point(-1, -1), 2, BorderType.Default, new MCvScalar());
    }

    private void ProcessContours(Mat foregroundMap)
    {
        using VectorOfVectorOfPoint contours = new();
        CvInvoke.FindContours(foregroundMap, contours, null, RetrType.External, ChainApproxMethod.ChainApproxSimple);

        var candidates = new List<(Rectangle Rect, double Area, VectorOfPoint Contour)>();
        for (int i = 0; i < contours.Size; i++)
        {
            double area = CvInvoke.ContourArea(contours[i]);
            if (area > MIN_AREA_THRESHOLD && area < MAX_AREA_THRESHOLD)
            {
                candidates.Add((CvInvoke.BoundingRectangle(contours[i]), area, new VectorOfPoint(contours[i].ToArray())));
            }
        }

        var sorted = candidates.OrderByDescending(c => c.Area).ToList();
        var accepted = new List<Rectangle>();

        foreach (var (rect, area, contour) in sorted)
        {
            Point center = new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            if (accepted.Any(r => r.Contains(center))) continue;

            accepted.Add(rect);
            CvInvoke.Rectangle(OriginalWithDetected, rect, new MCvScalar(0, 0, 255), 12);

            var extracted = ExtractPhotoFromContour(contour);
            if (extracted != null && !extracted.IsEmpty)
            {
                DetectedPhotos.Add(extracted);
            }
        }
    }

    private Mat ExtractPhotoFromContour(VectorOfPoint contour)
    {
        // NO ROTATION: We perform a direct upright crop on the original scan
        Rectangle rect = CvInvoke.BoundingRectangle(contour);
        
        // Ensure we stay inside the physical image pixels
        rect.Intersect(new Rectangle(Point.Empty, Original.Size));
        
        if (rect.Width <= 10 || rect.Height <= 10) return new Mat();

        // Return a direct copy of that area from the scan
        return new Mat(Original, rect).Clone();
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
