using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using System.Drawing;
using Emgu.CV.Util;
using System.Linq;
using System;

namespace PhotoCropper;

public class PhotoCropper : IDisposable
{
    private readonly double MIN_AREA_THRESHOLD = 0;
    private readonly double MAX_AREA_THRESHOLD = 0;
    private bool disposedValue;

    public string OriginalFilePath { get; }
    public double BackgroundTolerance { get; set; } = 30;

    public PhotoCropper(string originalFilePath)
    {
        this.OriginalFilePath = originalFilePath;
        Original = CvInvoke.Imread(originalFilePath, ImreadModes.ColorRgb);

        // Minimum 1% of scan, maximum 90%
        MIN_AREA_THRESHOLD = (Original.Width * Original.Height) * 0.01;
        MAX_AREA_THRESHOLD = (Original.Width * Original.Height) * 0.90;

        OriginalWithDetected = Original.Clone();
    }

    public Mat Original { get; set; }
    public Mat OriginalWithDetected { get; set; }
    public List<Mat> DetectedPhotos { get; set; } = [];
    public List<bool> DiscardedFlags { get; set; } = [];

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
        DiscardedFlags.Clear();

        OriginalWithDetected?.Dispose();
        OriginalWithDetected = Original.Clone();
    }

    private MCvScalar SampleBackgroundColor(Mat hsv)
    {
        int s = 15; // sample size
        // Ensure we have enough space to sample corners
        if (hsv.Width < s * 2 + 10 || hsv.Height < s * 2 + 10) return new MCvScalar();

        var samples = new List<MCvScalar> {
            CvInvoke.Mean(new Mat(hsv, new Rectangle(5, 5, s, s))),
            CvInvoke.Mean(new Mat(hsv, new Rectangle(hsv.Width - s - 5, 5, s, s))),
            CvInvoke.Mean(new Mat(hsv, new Rectangle(5, hsv.Height - s - 5, s, s))),
            CvInvoke.Mean(new Mat(hsv, new Rectangle(hsv.Width - s - 5, hsv.Height - s - 5, s, s)))
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
        using Mat openKernel = CvInvoke.GetStructuringElement(MorphShapes.Rectangle, new Size(5, 5), new Point(-1, -1));
        CvInvoke.MorphologyEx(foreground, foreground, MorphOp.Open, openKernel, new Point(-1, -1), 1, BorderType.Default, new MCvScalar());

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

            using VectorOfPoint hull = new();
            CvInvoke.ConvexHull(Contour, hull);

            RotatedRect rr = CvInvoke.MinAreaRect(hull);
            PointF[] vertices = rr.GetVertices();
            for (int j = 0; j < 4; j++)
            {
                CvInvoke.Line(OriginalWithDetected, Point.Round(vertices[j]), Point.Round(vertices[(j + 1) % 4]), new MCvScalar(0, 0, 255), 12);
            }

            var extracted = ExtractPhotoFromContour(hull);
            if (extracted != null && !extracted.IsEmpty)
            {
                DetectedPhotos.Add(extracted);
                DiscardedFlags.Add(false);
            }
        }
    }

    private Mat ExtractPhotoFromContour(VectorOfPoint hull)
    {
        RotatedRect rect = CvInvoke.MinAreaRect(hull);
        float angle = rect.Angle;
        SizeF size = rect.Size;

        // 1. Normalize angle and swap dimensions if needed
        if (size.Width < size.Height)
        {
            angle += 90;
            (size.Height, size.Width) = (size.Width, size.Height);
        }

        // --- THE "EXPAND" ---
        // Add a 2-pixel safety margin
        size.Width += 4;
        size.Height += 4;

        // 2. Extract a SQUARE ROI to prevent clipping during rotation
        // A square with side = Max(Width, Height) * 1.5 ensures plenty of room for any rotation
        float maxDim = Math.Max(rect.Size.Width, rect.Size.Height);
        int side = (int)(maxDim * 1.5);
        
        Rectangle roi = new Rectangle(
            (int)(rect.Center.X - side / 2.0),
            (int)(rect.Center.Y - side / 2.0),
            side,
            side
        );
        
        // Safety intersection with original scan boundaries
        Rectangle scanBounds = new Rectangle(Point.Empty, Original.Size);
        Rectangle safeRoi = Rectangle.Intersect(roi, scanBounds);

        if (safeRoi.Width <= 10 || safeRoi.Height <= 10) return new Mat();

        // Create the local piece from the scan
        using Mat scanRoi = new Mat(Original, safeRoi);
        
        // Create a perfectly square canvas and paste the scanRoi into it
        // This ensures the photo is centered in a large enough square to rotate 360 degrees
        using Mat squareCanvas = new Mat(side, side, DepthType.Cv8U, 3);
        squareCanvas.SetTo(new MCvScalar(255, 255, 255)); // White fill
        
        int destX = Math.Max(0, safeRoi.X - roi.X);
        int destY = Math.Max(0, safeRoi.Y - roi.Y);
        Rectangle destRect = new Rectangle(destX, destY, safeRoi.Width, safeRoi.Height);
        scanRoi.CopyTo(new Mat(squareCanvas, destRect));

        // 3. Local pivot is exactly the center of our square canvas
        PointF localCenter = new PointF(side / 2.0f, side / 2.0f);

        // 4. Rotate the square canvas
        using Mat rotationMatrix = new();
        CvInvoke.GetRotationMatrix2D(localCenter, angle, 1.0, rotationMatrix);

        using Mat rotatedCanvas = new();
        CvInvoke.WarpAffine(squareCanvas, rotatedCanvas, rotationMatrix, squareCanvas.Size, Inter.Linear, Warp.Default, BorderType.Constant, new MCvScalar(255, 255, 255));

        // 5. Final straight crop
        int x = (int)Math.Max(0, Math.Round(localCenter.X - size.Width / 2.0));
        int y = (int)Math.Max(0, Math.Round(localCenter.Y - size.Height / 2.0));
        int w = (int)Math.Round(size.Width);
        int h = (int)Math.Round(size.Height);
        
        Rectangle cropArea = new Rectangle(x, y, w, h);
        cropArea.Intersect(new Rectangle(Point.Empty, rotatedCanvas.Size));

        if (cropArea.Width <= 10 || cropArea.Height <= 10) return new Mat();

        return new Mat(rotatedCanvas, cropArea).Clone();
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
        DiscardedFlags.RemoveAt(index);
    }

    public void AddManualCrop(Rectangle rect)
    {
        Rectangle searchRoi = new Rectangle(rect.X - 20, rect.Y - 20, rect.Width + 40, rect.Height + 40);
        searchRoi.Intersect(new Rectangle(Point.Empty, Original.Size));

        if (searchRoi.Width <= 10 || searchRoi.Height <= 10) return;

        using Mat roiMat = new Mat(Original, searchRoi);
        using Mat hsv = new();
        CvInvoke.CvtColor(roiMat, hsv, ColorConversion.Bgr2Hsv);

        MCvScalar avgBackgroundColor = SampleBackgroundColor(hsv);
        using Mat backgroundMask = CreateBackgroundMask(hsv, avgBackgroundColor);
        
        using Mat gray = new();
        CvInvoke.CvtColor(roiMat, gray, ColorConversion.Bgr2Gray);
        using Mat edges = new();
        CvInvoke.GaussianBlur(gray, edges, new Size(5, 5), 1.5);
        CvInvoke.Canny(edges, edges, 20, 50);

        using Mat foreground = CreateForegroundMap(backgroundMask, edges);
        RefineForegroundMap(foreground);

        using VectorOfVectorOfPoint contours = new();
        CvInvoke.FindContours(foreground, contours, null, RetrType.External, ChainApproxMethod.ChainApproxSimple);

        VectorOfPoint? bestHull = null;
        double maxArea = 0;

        for (int i = 0; i < contours.Size; i++)
        {
            double area = CvInvoke.ContourArea(contours[i]);
            if (area > maxArea)
            {
                maxArea = area;
                bestHull = new VectorOfPoint();
                CvInvoke.ConvexHull(contours[i], bestHull);
            }
        }

        if (bestHull != null)
        {
            Point[] points = bestHull.ToArray();
            for (int i = 0; i < points.Length; i++)
            {
                points[i].X += searchRoi.X;
                points[i].Y += searchRoi.Y;
            }
            using VectorOfPoint globalHull = new VectorOfPoint(points);

            var extracted = ExtractPhotoFromContour(globalHull);
            if (extracted != null && !extracted.IsEmpty)
            {
                DetectedPhotos.Add(extracted);
                DiscardedFlags.Add(false);
                return;
            }
        }

        rect.Intersect(new Rectangle(Point.Empty, Original.Size));
        if (rect.Width > 10 && rect.Height > 10)
        {
            DetectedPhotos.Add(new Mat(Original, rect).Clone());
            DiscardedFlags.Add(false);
        }
    }

    public void SaveDetectedPhotos()
    {
        string? directory = Path.GetDirectoryName(OriginalFilePath);
        if (string.IsNullOrEmpty(directory)) return;

        string outputFolder = Path.Combine(directory, "cropped");
        Directory.CreateDirectory(outputFolder);

        string baseFileName = Path.GetFileNameWithoutExtension(OriginalFilePath);

        int saveCounter = 1;
        for (int i = 0; i < DetectedPhotos.Count; i++)
        {
            if (DetectedPhotos[i].IsEmpty || DiscardedFlags[i]) continue;
            string fileName = Path.Combine(outputFolder, $"{baseFileName}_{saveCounter++}.jpg");
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
