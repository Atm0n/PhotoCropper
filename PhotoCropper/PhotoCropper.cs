using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using System.Drawing;
using Emgu.CV.Util;

namespace PhotoCropper;

public class PhotoCropper : IDisposable
{
    #region Fields & Properties

    private bool disposedValue;

    public string OriginalFilePath { get; }
    
    // Configurable Detection Parameters
    public double BackgroundTolerance { get; set; } = 30;
    public double MinAreaFactor { get; set; } = 0.01; // 1% of scan
    public double MaxAreaFactor { get; set; } = 0.90; // 90% of scan
    public double CannyLowThreshold { get; set; } = 20;
    public double CannyHighThreshold { get; set; } = 50;

    public Mat Original { get; set; }
    public Mat OriginalWithDetected { get; set; }
    public List<Mat> DetectedPhotos { get; set; } = [];

    #endregion

    #region Initialization & Disposal

    public PhotoCropper(string originalFilePath)
    {
        this.OriginalFilePath = originalFilePath;
        Original = CvInvoke.Imread(originalFilePath, ImreadModes.AnyColor);

        OriginalWithDetected = Original.Clone();
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

    #endregion

    #region Detection & Extraction

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

    private MCvScalar SampleBackgroundColor(Mat hsv)
    {
        int s = 15; // sample size
        if (hsv.Width < s * 2 + 10 || hsv.Height < s * 2 + 10) return new MCvScalar();

        // Sample 4 corners efficiently using ROI without creating new Mat objects
        var r1 = new Rectangle(5, 5, s, s);
        var r2 = new Rectangle(hsv.Width - s - 5, 5, s, s);
        var r3 = new Rectangle(5, hsv.Height - s - 5, s, s);
        var r4 = new Rectangle(hsv.Width - s - 5, hsv.Height - s - 5, s, s);

        using Mat m1 = new(hsv, r1);
        using Mat m2 = new(hsv, r2);
        using Mat m3 = new(hsv, r3);
        using Mat m4 = new(hsv, r4);

        var s1 = CvInvoke.Mean(m1);
        var s2 = CvInvoke.Mean(m2);
        var s3 = CvInvoke.Mean(m3);
        var s4 = CvInvoke.Mean(m4);

        return new MCvScalar(
            (s1.V0 + s2.V0 + s3.V0 + s4.V0) / 4.0,
            (s1.V1 + s2.V1 + s3.V1 + s4.V1) / 4.0,
            (s1.V2 + s2.V2 + s3.V2 + s4.V2) / 4.0
        );
    }

    private Mat CreateBackgroundMask(Mat hsv, MCvScalar avgColor, double? toleranceOverride = null)
    {
        double tol = toleranceOverride ?? BackgroundTolerance;
        double hTol = tol * 0.4;
        double sTol = tol;
        double vTol = tol;

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
        CvInvoke.Canny(edges, edges, CannyLowThreshold, CannyHighThreshold);
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

        double totalArea = Original.Width * Original.Height;
        double minArea = totalArea * MinAreaFactor;
        double maxArea = totalArea * MaxAreaFactor;

        var candidates = new List<(Rectangle Rect, double Area, VectorOfPoint Contour)>();
        for (int i = 0; i < contours.Size; i++)
        {
            double area = CvInvoke.ContourArea(contours[i]);
            if (area > minArea && area < maxArea)
            {
                candidates.Add((CvInvoke.BoundingRectangle(contours[i]), area, new VectorOfPoint(contours[i].ToArray())));
            }
        }

        var sorted = candidates.OrderByDescending(c => c.Area).ToList();
        var acceptedHulls = new List<VectorOfPoint>();
        var acceptedRects = new List<Rectangle>();

        foreach (var (Rect, Area, Contour) in sorted)
        {
            Point center = new(Rect.X + Rect.Width / 2, Rect.Y + Rect.Height / 2);
            if (acceptedRects.Any(r => r.Contains(center))) continue;

            acceptedRects.Add(Rect);
            
            VectorOfPoint hull = new();
            CvInvoke.ConvexHull(Contour, hull);
            acceptedHulls.Add(hull);

            RotatedRect rr = CvInvoke.MinAreaRect(hull);
            PointF[] vertices = rr.GetVertices();
            for (int j = 0; j < 4; j++)
            {
                CvInvoke.Line(OriginalWithDetected, Point.Round(vertices[j]), Point.Round(vertices[(j + 1) % 4]), new MCvScalar(0, 0, 255), 12);
            }
        }

        // Parallel extraction: Rotate and crop each photo on different CPU cores
        Mat?[] results = new Mat[acceptedHulls.Count];
        Parallel.For(0, acceptedHulls.Count, i => 
        {
            results[i] = ExtractPhotoFromContour(acceptedHulls[i]);
            acceptedHulls[i].Dispose();
        });

        foreach (var mat in results)
        {
            if (mat != null && !mat.IsEmpty)
            {
                DetectedPhotos.Add(mat);
            }
        }
    }

    private Mat ExtractPhotoFromContour(VectorOfPoint hull)
    {
        RotatedRect rect = CvInvoke.MinAreaRect(hull);
        float angle = rect.Angle;
        SizeF size = rect.Size;

        if (size.Width < size.Height)
        {
            angle += 90;
            (size.Height, size.Width) = (size.Width, size.Height);
        }

        float maxDim = Math.Max(rect.Size.Width, rect.Size.Height);
        int side = (int)(maxDim * 1.5);
        
        Rectangle roi = new(
            (int)(rect.Center.X - side / 2.0),
            (int)(rect.Center.Y - side / 2.0),
            side,
            side
        );
        
        Rectangle scanBounds = new(Point.Empty, Original.Size);
        Rectangle safeRoi = Rectangle.Intersect(roi, scanBounds);

        if (safeRoi.Width <= 10 || safeRoi.Height <= 10) return new Mat();

        using Mat scanRoi = new(Original, safeRoi);
        using Mat squareCanvas = new(side, side, DepthType.Cv8U, 3);
        squareCanvas.SetTo(new MCvScalar(255, 255, 255)); // White fill
        
        int destX = Math.Max(0, safeRoi.X - roi.X);
        int destY = Math.Max(0, safeRoi.Y - roi.Y);
        Rectangle destRect = new(destX, destY, safeRoi.Width, safeRoi.Height);
        
        // Use a sub-mat for direct copy without overhead
        using Mat canvasRoi = new(squareCanvas, destRect);
        scanRoi.CopyTo(canvasRoi);

        PointF localCenter = new(side / 2.0f, side / 2.0f);
        using Mat rotationMatrix = new();
        CvInvoke.GetRotationMatrix2D(localCenter, angle, 1.0, rotationMatrix);

        Mat rotatedCanvas = new();
        CvInvoke.WarpAffine(squareCanvas, rotatedCanvas, rotationMatrix, squareCanvas.Size, Inter.Cubic, Warp.Default, BorderType.Constant, new MCvScalar(255, 255, 255));

        using Mat gray = new();
        CvInvoke.CvtColor(rotatedCanvas, gray, ColorConversion.Bgr2Gray);
        
        using Mat contentMask = new();
        CvInvoke.Threshold(gray, contentMask, 253, 255, ThresholdType.BinaryInv);
        
        Rectangle snugRect = CvInvoke.BoundingRectangle(contentMask);
        
        Rectangle predictedRect = new(
            (int)Math.Max(0, Math.Round(localCenter.X - size.Width / 2.0)),
            (int)Math.Max(0, Math.Round(localCenter.Y - size.Height / 2.0)),
            (int)Math.Round(size.Width),
            (int)Math.Round(size.Height)
        );
        
        Rectangle finalCrop = Rectangle.Intersect(snugRect, predictedRect);
        finalCrop.Intersect(new Rectangle(Point.Empty, rotatedCanvas.Size));

        if (finalCrop.Width <= 10 || finalCrop.Height <= 10) 
        {
            rotatedCanvas.Dispose();
            return new Mat();
        }

        // Return a fresh copy of the region and dispose the large rotated canvas
        Mat final = new Mat(rotatedCanvas, finalCrop).Clone();
        rotatedCanvas.Dispose();
        return final;
    }

    #endregion

    #region Manual Edits & Refinement

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

    public System.Drawing.Rectangle GetRefinedCropRect(int index)
    {
        if (index < 0 || index >= DetectedPhotos.Count) return System.Drawing.Rectangle.Empty;

        Mat photo = DetectedPhotos[index];
        using Mat gray = new();
        CvInvoke.CvtColor(photo, gray, ColorConversion.Bgr2Gray);
        
        // 1. Determine the background color by sampling the 4 extreme corners.
        int s = 5;
        if (gray.Width <= s * 2 || gray.Height <= s * 2) return new Rectangle(0, 0, photo.Width, photo.Height);

        var tl = CvInvoke.Mean(new Mat(gray, new Rectangle(0, 0, s, s))).V0;
        var tr = CvInvoke.Mean(new Mat(gray, new Rectangle(gray.Width - s, 0, s, s))).V0;
        var bl = CvInvoke.Mean(new Mat(gray, new Rectangle(0, gray.Height - s, s, s))).V0;
        var br = CvInvoke.Mean(new Mat(gray, new Rectangle(gray.Width - s, gray.Height - s, s, s))).V0;

        // Average of corners to find the margin's specific shade of grey
        double bgGray = (tl + tr + bl + br) / 4.0;

        using Mat mask = new();
        // Tolerance of 20 shades of grey to catch shadows/gradients in the scanner margin
        double lower = Math.Max(0, bgGray - 20);
        double upper = Math.Min(255, bgGray + 20);

        CvInvoke.InRange(gray, new ScalarArray(lower), new ScalarArray(upper), mask);
        
        // Invert: Photo is white (255), background margin is black (0)
        CvInvoke.BitwiseNot(mask, mask);

        // Remove scanner noise/dust from the black margin
        using Mat openKernel = CvInvoke.GetStructuringElement(MorphShapes.Rectangle, new Size(7, 7), new Point(-1, -1));
        CvInvoke.MorphologyEx(mask, mask, MorphOp.Open, openKernel, new Point(-1, -1), 1, BorderType.Default, new MCvScalar());

        // Solidify the photo area
        using Mat closeKernel = CvInvoke.GetStructuringElement(MorphShapes.Rectangle, new Size(21, 21), new Point(-1, -1));
        CvInvoke.MorphologyEx(mask, mask, MorphOp.Close, closeKernel, new Point(-1, -1), 3, BorderType.Default, new MCvScalar());

        Rectangle contentBox = CvInvoke.BoundingRectangle(mask);
        
        // Safety: We lower the abort threshold to 20% to allow aggressive trimming of thick borders
        if (contentBox.Width < photo.Width * 0.2 || contentBox.Height < photo.Height * 0.2)
        {
            return new Rectangle(0, 0, photo.Width, photo.Height);
        }

        // Shave 2 pixels to guarantee we cut inside the gradient edge of the margin
        contentBox.Inflate(-2, -2);
        
        int x = Math.Max(0, contentBox.X);
        int y = Math.Max(0, contentBox.Y);
        int w = Math.Max(10, contentBox.Width);
        int h = Math.Max(10, contentBox.Height);
        
        Rectangle finalRect = new(x, y, w, h);
        finalRect.Intersect(new Rectangle(Point.Empty, photo.Size));

        return finalRect;
    }

    public void ApplyCropToPhoto(int index, System.Drawing.Rectangle rect)
    {
        if (index < 0 || index >= DetectedPhotos.Count) return;
        
        Mat photo = DetectedPhotos[index];
        rect.Intersect(new System.Drawing.Rectangle(Point.Empty, photo.Size));
        if (rect.Width <= 10 || rect.Height <= 10) return;

        Mat cropped = new Mat(photo, rect).Clone();
        DetectedPhotos[index].Dispose();
        DetectedPhotos[index] = cropped;
    }

    public void AddManualCrop(Rectangle rect)
    {
        Rectangle searchRoi = new(rect.X - 20, rect.Y - 20, rect.Width + 40, rect.Height + 40);
        searchRoi.Intersect(new Rectangle(Point.Empty, Original.Size));

        if (searchRoi.Width <= 10 || searchRoi.Height <= 10) return;

        using Mat roiMat = new(Original, searchRoi);
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
            using VectorOfPoint globalHull = new(points);

            var extracted = ExtractPhotoFromContour(globalHull);
            if (extracted != null && !extracted.IsEmpty)
            {
                DetectedPhotos.Add(extracted);
                return;
            }
        }

        rect.Intersect(new Rectangle(Point.Empty, Original.Size));
        if (rect.Width > 10 && rect.Height > 10)
        {
            DetectedPhotos.Add(new Mat(Original, rect).Clone());
        }
    }

    #endregion

    #region File Operations

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
            if (DetectedPhotos[i].IsEmpty) continue;
            string fileName = Path.Combine(outputFolder, $"{baseFileName}_{saveCounter++}.jpg");
            
            DetectedPhotos[i].Save(fileName);
        }
    }

    #endregion
}