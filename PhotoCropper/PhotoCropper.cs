using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using System.Drawing;
using Emgu.CV.Util;
using System.Linq;

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
        // 1. Open to remove small dust/noise without rounding off main corners
        using Mat openKernel = CvInvoke.GetStructuringElement(MorphShapes.Rectangle, new Size(5, 5), new Point(-1, -1));
        CvInvoke.MorphologyEx(foreground, foreground, MorphOp.Open, openKernel, new Point(-1, -1), 1, BorderType.Default, new MCvScalar());

        // 2. Close to solidify the photo and keep edges straight
        using Mat closeKernel = CvInvoke.GetStructuringElement(MorphShapes.Rectangle, new Size(11, 11), new Point(-1, -1));
        CvInvoke.MorphologyEx(foreground, foreground, MorphOp.Close, closeKernel, new Point(-1, -1), 2, BorderType.Default, new MCvScalar());
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

        foreach (var (Rect, Area, Contour) in sorted)
        {
            Point center = new(Rect.X + Rect.Width / 2, Rect.Y + Rect.Height / 2);
            if (accepted.Any(r => r.Contains(center))) continue;

            accepted.Add(Rect);

            // Use Convex Hull for a much more stable angle calculation
            using VectorOfPoint hull = new();
            CvInvoke.ConvexHull(Contour, hull);

            // 1. Get the perfect mathematical rectangle (with angle)
            RotatedRect rr = CvInvoke.MinAreaRect(hull);

            // 2. Draw the visual marker as 4 tilted lines
            PointF[] vertices = rr.GetVertices();
            for (int j = 0; j < 4; j++)
            {
                CvInvoke.Line(OriginalWithDetected, Point.Round(vertices[j]), Point.Round(vertices[(j + 1) % 4]), new MCvScalar(0, 0, 255), 12);
            }

            var extracted = ExtractPhotoFromContour(hull);
            if (extracted != null && !extracted.IsEmpty)
            {
                DetectedPhotos.Add(extracted);
            }
        }
    }

    private Mat ExtractPhotoFromContour(VectorOfPoint hull)
    {
        // 1. Identify the exact angle and size of the tilted photo using the hull
        RotatedRect rect = CvInvoke.MinAreaRect(hull);
        float angle = rect.Angle;
        SizeF size = rect.Size;

        // 2. Normalize the angle to ensure the photo is upright
        // OpenCV angles can be tricky; we ensure the wider side is horizontal
        if (size.Width < size.Height)
        {
            angle += 90;
            (size.Height, size.Width) = (size.Width, size.Height);
        }

        // --- THE "SHAVE" ---
        // Mathematically shrink the final cut by 6 pixels on all sides (12 total)
        // to cleanly remove the scanner shadow margin without distorting the angle.
        size.Width = Math.Max(10, size.Width - 12);
        size.Height = Math.Max(10, size.Height - 12);

        // 3. Mathematical Rotation
        // We create a 'Rotation Matrix' centered on the photo
        using Mat rotationMatrix = new();
        CvInvoke.GetRotationMatrix2D(rect.Center, angle, 1.0, rotationMatrix);

        // 4. Transform the entire scan to straighten this specific photo
        using Mat rotatedFullImage = new();
        CvInvoke.WarpAffine(Original, rotatedFullImage, rotationMatrix, Original.Size, Inter.Linear, Warp.Default, BorderType.Constant, new MCvScalar(255, 255, 255));

        // 5. Clean Crop
        Rectangle cropArea = new(
            (int)(rect.Center.X - size.Width / 2.0),
            (int)(rect.Center.Y - size.Height / 2.0),
            (int)size.Width,
            (int)size.Height
        );

        // Safety intersection with image boundaries
        cropArea.Intersect(new Rectangle(Point.Empty, rotatedFullImage.Size));

        if (cropArea.Width <= 10 || cropArea.Height <= 10) return new Mat();

        return new Mat(rotatedFullImage, cropArea).Clone();
    }

    public void RotatePhoto(int index)
    {
        if (index < 0 || index >= DetectedPhotos.Count) return;

        Mat rotated = new();
        CvInvoke.Rotate(DetectedPhotos[index], rotated, RotateFlags.Rotate90Clockwise);
        
        // Dispose old and replace with new
        DetectedPhotos[index].Dispose();
        DetectedPhotos[index] = rotated;
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
