using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using System.Drawing;
using Emgu.CV.Util;
using System.Collections.ObjectModel;

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

    public void DetectPhotos()
    {
        ResetState();

        // Sample background color before padding
        using Mat hsv = new();
        CvInvoke.CvtColor(Original, hsv, ColorConversion.Bgr2Hsv);
        MCvScalar avgBackgroundColorHsv = CustomBackgroundColorHsv ?? SampleBackgroundColor(hsv);

        // Convert HSV background color to BGR for border padding
        using Mat hsvPixel = new(1, 1, DepthType.Cv8U, 3);
        hsvPixel.SetTo(avgBackgroundColorHsv);
        using Mat bgrPixel = new();
        CvInvoke.CvtColor(hsvPixel, bgrPixel, ColorConversion.Hsv2Bgr);
        MCvScalar bgBgr = CvInvoke.Mean(bgrPixel);

        // Pad the image with a synthetic margin so photos touching or extending to the scan boundary form complete closed contours
        int pad = Math.Max(20, (Math.Min(Original.Width, Original.Height) / 50));
        using Mat padded = new();
        CvInvoke.CopyMakeBorder(Original, padded, pad, pad, pad, pad, BorderType.Constant, bgBgr);

        // Multi-pass sensitivity detection:
        // When photos require subtle sensitivity adjustments (e.g. 27%, 31% vs 25% default),
        // or when photos occupy only a portion of the scan, evaluate fine-grained progressive sensitivity steps
        // (both fine steps nearby +2, +4, +6, +8, +12, +18, +25, +35 and slight tightening -5, -10).
        var candidateDetections = new List<(Point[] shapePoints, Rectangle rect, double score, RotatedRect rotated, double area, double rectangularity, double convexity)>();
        double baseTol = BackgroundTolerance;
        double totalScanArea = (double)Original.Width * Original.Height;

        // Fine-grained search tolerances around base tolerance
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
            PopulateForegroundMask(padded, foreground, avgBackgroundColorHsv, tol, CannyLowThreshold, CannyHighThreshold);
            
            var passCandidates = ExtractCandidates(foreground, pad);
            foreach (var cand in passCandidates)
            {
                candidateDetections.Add(cand);
            }
        }

        ProcessAndFilterCandidates(candidateDetections, padded, pad);
    }

    private List<(Point[] shapePoints, Rectangle rect, double score, RotatedRect rotated, double area, double rectangularity, double convexity)> ExtractCandidates(Mat foregroundMap, int padOffset)
    {
        var result = new List<(Point[] shapePoints, Rectangle rect, double score, RotatedRect rotated, double area, double rectangularity, double convexity)>();

        using VectorOfVectorOfPoint contours = new();
        CvInvoke.FindContours(foregroundMap, contours, null, RetrType.External, ChainApproxMethod.ChainApproxSimple);

        double totalArea = Original.Width * Original.Height;
        double minArea = totalArea * MinAreaFactor;
        double maxArea = totalArea * MaxAreaFactor;

        for (int i = 0; i < contours.Size; i++)
        {
            double contourArea = CvInvoke.ContourArea(contours[i]);
            if (contourArea < minArea || contourArea > maxArea) continue;

            // Convex hull of initial contour
            using VectorOfPoint hull = new();
            CvInvoke.ConvexHull(contours[i], hull);
            double hullArea = CvInvoke.ContourArea(hull);

            // Polygon approximation to extract clean quadrilateral boundaries
            double peri = CvInvoke.ArcLength(hull, true);
            using VectorOfPoint approx = new();
            CvInvoke.ApproxPolyDP(hull, approx, 0.02 * peri, true);

            // If approximation has 4 to 8 vertices and is convex, use it; otherwise fallback to hull
            Point[] rawPoints = (approx.Size >= 4 && approx.Size <= 8 && CvInvoke.IsContourConvex(approx)) 
                ? approx.ToArray() 
                : hull.ToArray();

            // Map coordinates back from padded space to Original image space
            Point[] shapePoints = new Point[rawPoints.Length];
            for (int p = 0; p < rawPoints.Length; p++)
            {
                shapePoints[p] = new Point(
                    Math.Clamp(rawPoints[p].X - padOffset, 0, Original.Width - 1),
                    Math.Clamp(rawPoints[p].Y - padOffset, 0, Original.Height - 1)
                );
            }

            using VectorOfPoint tempShape = new(shapePoints);
            RotatedRect rr = CvInvoke.MinAreaRect(tempShape);
            
            // Aspect ratio / compactness filter to discard thin line artifacts
            float w = rr.Size.Width;
            float h = rr.Size.Height;
            if (w < 20 || h < 20) continue;

            float aspectRatio = Math.Max(w, h) / Math.Max(1.0f, Math.Min(w, h));
            if (aspectRatio > 20.0f) continue; // Extreme thin strip rejection

            // Rectangularity score: Ratio of contour area to its minimum bounding rotated rectangle area
            double rrArea = Math.Max(1.0, (double)w * h);
            double rectangularity = Math.Clamp(contourArea / rrArea, 0.0, 1.0);

            // Convexity / solidity score: Clean single photos have high convexity (~1.0), merged photos have waist indents (<0.92)
            double convexity = Math.Clamp(contourArea / Math.Max(1.0, hullArea), 0.0, 1.0);

            // Quality score heavily favors clean, rectangular, convex single photos over merged composites
            double quality = Math.Pow(rectangularity, 3) * Math.Pow(convexity, 2);
            double score = contourArea * quality;

            result.Add((shapePoints, CvInvoke.BoundingRectangle(tempShape), score, rr, contourArea, rectangularity, convexity));
        }

        return result;
    }

    private static double CalculatePolygonIntersectionArea(Point[] poly1, Rectangle bounds1, Point[] poly2, Rectangle bounds2)
    {
        Rectangle intersectBox = Rectangle.Intersect(bounds1, bounds2);
        if (intersectBox.IsEmpty || intersectBox.Width <= 0 || intersectBox.Height <= 0)
        {
            return 0;
        }

        using Mat mask1 = new(intersectBox.Size, DepthType.Cv8U, 1);
        using Mat mask2 = new(intersectBox.Size, DepthType.Cv8U, 1);
        using Mat maskOverlap = new();
        mask1.SetTo(new MCvScalar(0));
        mask2.SetTo(new MCvScalar(0));

        Point[] shifted1 = poly1.Select(p => new Point(p.X - intersectBox.X, p.Y - intersectBox.Y)).ToArray();
        Point[] shifted2 = poly2.Select(p => new Point(p.X - intersectBox.X, p.Y - intersectBox.Y)).ToArray();

        using (VectorOfPoint vp1 = new(shifted1))
        using (VectorOfPoint vp2 = new(shifted2))
        using (VectorOfVectorOfPoint vvp1 = new(vp1))
        using (VectorOfVectorOfPoint vvp2 = new(vp2))
        {
            CvInvoke.FillPoly(mask1, vvp1, new MCvScalar(255));
            CvInvoke.FillPoly(mask2, vvp2, new MCvScalar(255));
        }

        CvInvoke.BitwiseAnd(mask1, mask2, maskOverlap);
        return CvInvoke.CountNonZero(maskOverlap);
    }

    private void ProcessAndFilterCandidates(
        List<(Point[] shapePoints, Rectangle rect, double score, RotatedRect rotated, double area, double rectangularity, double convexity)> candidates, 
        Mat paddedSource, 
        int padOffset)
    {
        // 1. Composite resolution: Discard large merged candidate boxes that encompass 2 or more distinct sub-candidates
        var compositeIndices = new HashSet<int>();
        for (int i = 0; i < candidates.Count; i++)
        {
            var parent = candidates[i];
            var subCandidates = new List<int>();

            for (int j = 0; j < candidates.Count; j++)
            {
                if (i == j) continue;
                var child = candidates[j];

                // Child must be distinctly smaller than parent and have reasonable rectangularity
                if (child.area >= parent.area * 0.85 || child.area < parent.area * 0.15) continue;
                if (child.rectangularity < 0.65) continue;

                double overlap = CalculatePolygonIntersectionArea(child.shapePoints, child.rect, parent.shapePoints, parent.rect);
                // Child is mostly contained inside parent
                if (overlap / child.area >= 0.70)
                {
                    subCandidates.Add(j);
                }
            }

            // Check if parent contains at least 2 mutually disjoint sub-candidates
            if (subCandidates.Count >= 2)
            {
                bool foundDisjointPair = false;
                for (int a = 0; a < subCandidates.Count && !foundDisjointPair; a++)
                {
                    var candA = candidates[subCandidates[a]];
                    for (int b = a + 1; b < subCandidates.Count; b++)
                    {
                        var candB = candidates[subCandidates[b]];
                        double subOverlap = CalculatePolygonIntersectionArea(candA.shapePoints, candA.rect, candB.shapePoints, candB.rect);
                        double minSubArea = Math.Min(candA.area, candB.area);

                        // If two sub-candidates don't heavily overlap each other and their combined area accounts for > 45% of parent
                        if (subOverlap / minSubArea < 0.25 && (candA.area + candB.area) >= parent.area * 0.45)
                        {
                            foundDisjointPair = true;
                            break;
                        }
                    }
                }

                if (foundDisjointPair)
                {
                    compositeIndices.Add(i);
                }
            }
        }

        var validCandidates = candidates
            .Where((_, idx) => !compositeIndices.Contains(idx))
            .OrderByDescending(c => c.score)
            .ToList();

        var acceptedPolys = new List<VectorOfPoint>();

        try
        {
            foreach (var (shapePoints, rect, score, rotated, area, rectangularity, convexity) in validCandidates)
            {
                Point center = new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
                
                // Overlap test: ensure center does not fall into an already accepted polygon
                bool insideAny = false;
                foreach (var accepted in acceptedPolys)
                {
                    if (CvInvoke.PointPolygonTest(accepted, center, false) >= 0)
                    {
                        insideAny = true;
                        break;
                    }
                }
                if (insideAny) continue;

                // Mask-based Polygon Intersection Check:
                // Prevents a larger detection from invading another photo while allowing genuinely adjacent tilted photos
                bool excessiveOverlap = false;
                Rectangle candBounds = rect;
                using VectorOfPoint shape = new(shapePoints);

                foreach (var accepted in acceptedPolys)
                {
                    Rectangle accBounds = CvInvoke.BoundingRectangle(accepted);
                    double overlapPixels = CalculatePolygonIntersectionArea(shapePoints, candBounds, accepted.ToArray(), accBounds);
                    double minPolyArea = Math.Min(CvInvoke.ContourArea(shape), CvInvoke.ContourArea(accepted));

                    // If overlap exceeds 15% of the smaller photo, reject the duplicate/invading candidate
                    if (minPolyArea > 0 && (overlapPixels / minPolyArea) > 0.15)
                    {
                        excessiveOverlap = true;
                        break;
                    }
                }
                if (excessiveOverlap) continue;
                
                acceptedPolys.Add(new VectorOfPoint(shapePoints));

                PointF[] vertices = rotated.GetVertices();
                for (int j = 0; j < 4; j++)
                {
                    CvInvoke.Line(OriginalWithDetected, Point.Round(vertices[j]), Point.Round(vertices[(j + 1) % 4]), new MCvScalar(0, 0, 255), 12);
                }
            }

            // Parallel extraction: Rotate and crop each photo on different CPU cores using the padded source
            Mat?[] results = new Mat[acceptedPolys.Count];
            Parallel.For(0, acceptedPolys.Count, i => 
            {
                results[i] = ExtractPhotoFromContour(acceptedPolys[i], paddedSource, padOffset);
            });

            foreach (var mat in results)
            {
                if (mat != null && !mat.IsEmpty)
                {
                    DetectedPhotos.Add(mat);
                }
            }
        }
        finally
        {
            foreach (var poly in acceptedPolys)
            {
                poly.Dispose();
            }
        }
    }

    private void PopulateForegroundMask(Mat source, Mat outputForeground, MCvScalar avgBackgroundColor, double backgroundTolerance, double lowThreshold, double highThreshold)
    {
        using Mat hsv = new();
        CvInvoke.CvtColor(source, hsv, ColorConversion.Bgr2Hsv);

        using Mat backgroundMask = CreateBackgroundMask(hsv, avgBackgroundColor, backgroundTolerance);

        using Mat gray = new();
        CvInvoke.CvtColor(source, gray, ColorConversion.Bgr2Gray);
        
        // Bilateral filter smooths internal photo textures while preserving sharp outer boundaries
        using Mat smoothed = new();
        CvInvoke.BilateralFilter(gray, smoothed, 9, 75, 75);
        
        using Mat edges = new();
        CvInvoke.Canny(smoothed, edges, lowThreshold, highThreshold);

        int minDim = Math.Min(source.Width, source.Height);

        // Seal faint low-contrast borders (e.g. white photo borders) using dynamic Adaptive Thresholding
        int adaptiveBlockSize = Math.Max(5, (minDim / 150) | 1); // Resolution-aware block size
        using Mat adaptive = new();
        CvInvoke.AdaptiveThreshold(smoothed, adaptive, 255, AdaptiveThresholdType.GaussianC, ThresholdType.BinaryInv, adaptiveBlockSize, 7);
        CvInvoke.BitwiseOr(edges, adaptive, edges);

        CvInvoke.BitwiseNot(backgroundMask, outputForeground);
        CvInvoke.BitwiseOr(outputForeground, edges, outputForeground);

        // Dynamically scale morphology kernels based on scan resolution/dimensions
        // Use an elliptical close kernel to seal internal edges without bridging narrow gaps between adjacent photos
        int openSize = Math.Max(3, (minDim / 400) | 1);  // Ensure odd integer, min 3
        int closeSize = Math.Max(3, (minDim / 500) | 1); // Ensure odd integer, min 3

        using Mat openKernel = CvInvoke.GetStructuringElement(MorphShapes.Rectangle, new Size(openSize, openSize), new Point(-1, -1));
        CvInvoke.MorphologyEx(outputForeground, outputForeground, MorphOp.Open, openKernel, new Point(-1, -1), 1, BorderType.Default, new MCvScalar());

        using Mat closeKernel = CvInvoke.GetStructuringElement(MorphShapes.Ellipse, new Size(closeSize, closeSize), new Point(-1, -1));
        CvInvoke.MorphologyEx(outputForeground, outputForeground, MorphOp.Close, closeKernel, new Point(-1, -1), 1, BorderType.Default, new MCvScalar());
    }

    private static MCvScalar SampleBackgroundColor(Mat hsv)
    {
        int s = 15; // sample size
        if (hsv.Width < s * 3 + 20 || hsv.Height < s * 3 + 20) return new MCvScalar();

        int halfW = hsv.Width / 2;
        int halfH = hsv.Height / 2;

        var rects = new Rectangle[]
        {
            new(5, 5, s, s),                               // Top-Left
            new(halfW - s / 2, 5, s, s),                   // Top-Center
            new(hsv.Width - s - 5, 5, s, s),               // Top-Right
            new(5, halfH - s / 2, s, s),                   // Middle-Left
            new(hsv.Width - s - 5, halfH - s / 2, s, s),   // Middle-Right
            new(5, hsv.Height - s - 5, s, s),              // Bottom-Left
            new(halfW - s / 2, hsv.Height - s - 5, s, s),  // Bottom-Center
            new(hsv.Width - s - 5, hsv.Height - s - 5, s, s) // Bottom-Right
        };

        var hVals = new List<double>();
        var sVals = new List<double>();
        var vVals = new List<double>();

        foreach (var r in rects)
        {
            using Mat m = new(hsv, r);
            var mean = CvInvoke.Mean(m);
            hVals.Add(mean.V0);
            sVals.Add(mean.V1);
            vVals.Add(mean.V2);
        }

        hVals.Sort();
        sVals.Sort();
        vVals.Sort();

        // Calculate median for Hue, Saturation, and Value to robustly reject outliers
        double medianH = (hVals[3] + hVals[4]) / 2.0;
        double medianS = (sVals[3] + sVals[4]) / 2.0;
        double medianV = (vVals[3] + vVals[4]) / 2.0;

        return new MCvScalar(medianH, medianS, medianV);
    }

    public void SetCustomBackgroundFromPixel(int x, int y)
    {
        if (Original.IsEmpty) return;

        x = Math.Clamp(x, 0, Original.Width - 1);
        y = Math.Clamp(y, 0, Original.Height - 1);

        int s = 5;
        int half = s / 2;
        int startX = Math.Max(0, x - half);
        int startY = Math.Max(0, y - half);
        int w = Math.Min(Original.Width - startX, s);
        int h = Math.Min(Original.Height - startY, s);

        using Mat sampledArea = new(Original, new Rectangle(startX, startY, w, h));
        using Mat hsvSampled = new();
        CvInvoke.CvtColor(sampledArea, hsvSampled, ColorConversion.Bgr2Hsv);
        CustomBackgroundColorHsv = CvInvoke.Mean(hsvSampled);
    }

    private Mat CreateBackgroundMask(Mat hsv, MCvScalar avgColor, double? toleranceOverride = null)
    {
        double tol = toleranceOverride ?? BackgroundTolerance;
        double hTol = tol * 0.4;
        double sTol = tol;
        
        // Widen Value (V) tolerance slightly to absorb scanner lid shadow gradients if the background is light
        double vTol = avgColor.V2 > 128 ? tol * 1.5 : tol;

        MCvScalar lower = new(Math.Max(0, avgColor.V0 - hTol), Math.Max(0, avgColor.V1 - sTol), Math.Max(0, avgColor.V2 - vTol));
        MCvScalar upper = new(Math.Min(180, avgColor.V0 + hTol), Math.Min(255, avgColor.V1 + sTol), Math.Min(255, avgColor.V2 + vTol));

        Mat mask = new();
        using ScalarArray lowerArray = new(lower);
        using ScalarArray upperArray = new(upper);
        CvInvoke.InRange(hsv, lowerArray, upperArray, mask);
        return mask;
    }



    private Mat ExtractPhotoFromContour(VectorOfPoint shapeInScanSpace, Mat? sourceImage = null, int padOffset = 0)
    {
        RotatedRect rect = CvInvoke.MinAreaRect(shapeInScanSpace);
        PointF[] srcPoints = OrderBoxPoints(rect.GetVertices());

        // If extracting from the padded source, shift the crop quad coordinates to padded space
        if (sourceImage != null && padOffset > 0)
        {
            for (int i = 0; i < srcPoints.Length; i++)
            {
                srcPoints[i] = new PointF(srcPoints[i].X + padOffset, srcPoints[i].Y + padOffset);
            }
        }

        Mat extractSource = sourceImage ?? Original;

        float widthA = (float)Math.Sqrt(Math.Pow(srcPoints[1].X - srcPoints[0].X, 2) + Math.Pow(srcPoints[1].Y - srcPoints[0].Y, 2));
        float widthB = (float)Math.Sqrt(Math.Pow(srcPoints[2].X - srcPoints[3].X, 2) + Math.Pow(srcPoints[2].Y - srcPoints[3].Y, 2));
        int targetWidth = (int)Math.Round(Math.Max(widthA, widthB));

        float heightA = (float)Math.Sqrt(Math.Pow(srcPoints[3].X - srcPoints[0].X, 2) + Math.Pow(srcPoints[3].Y - srcPoints[0].Y, 2));
        float heightB = (float)Math.Sqrt(Math.Pow(srcPoints[2].X - srcPoints[1].X, 2) + Math.Pow(srcPoints[2].Y - srcPoints[1].Y, 2));
        int targetHeight = (int)Math.Round(Math.Max(heightA, heightB));

        if (targetWidth <= 10 || targetHeight <= 10) return new Mat();

        // Orientation normalization: default to landscape orientation (standard photo convention)
        if (targetWidth < targetHeight)
        {
            // Rotate points 90 degrees counter-clockwise so the width becomes the larger dimension
            PointF temp = srcPoints[0];
            srcPoints[0] = srcPoints[1];
            srcPoints[1] = srcPoints[2];
            srcPoints[2] = srcPoints[3];
            srcPoints[3] = temp;
            (targetWidth, targetHeight) = (targetHeight, targetWidth);
        }

        PointF[] dstPoints =
        [
            new PointF(0, 0),
            new PointF(targetWidth - 1, 0),
            new PointF(targetWidth - 1, targetHeight - 1),
            new PointF(0, targetHeight - 1)
        ];

        using Mat bgraOriginal = new();
        if (extractSource.NumberOfChannels == 3)
        {
            CvInvoke.CvtColor(extractSource, bgraOriginal, ColorConversion.Bgr2Bgra);
        }
        else
        {
            extractSource.CopyTo(bgraOriginal);
        }

        using Mat perspectiveMatrix = CvInvoke.GetPerspectiveTransform(srcPoints, dstPoints);
        Mat result = new();
        // Transparent border: MCvScalar(0, 0, 0, 0) for alpha channel
        CvInvoke.WarpPerspective(bgraOriginal, result, perspectiveMatrix, new Size(targetWidth, targetHeight), Inter.Cubic, Warp.Default, BorderType.Constant, new MCvScalar(0, 0, 0, 0));

        return result;
    }

    private static PointF[] OrderBoxPoints(PointF[] pts)
    {
        // Sort points by x-coordinate
        var xSorted = pts.OrderBy(p => p.X).ToArray();

        // Grab left-most and right-most points
        var leftMost = xSorted.Take(2).OrderBy(p => p.Y).ToArray();
        var rightMost = xSorted.Skip(2).OrderBy(p => p.Y).ToArray();

        // [top-left, top-right, bottom-right, bottom-left]
        return [leftMost[0], rightMost[0], rightMost[1], leftMost[1]];
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

        Mat photo = DetectedPhotos[index];
        using Mat gray = new();
        CvInvoke.CvtColor(photo, gray, ColorConversion.Bgr2Gray);
        
        int s = 5;
        if (gray.Width <= s * 2 || gray.Height <= s * 2) return new Rectangle(0, 0, photo.Width, photo.Height);

        double bgGray = EstimateAverageBackgroundShade(gray);

        using Mat mask = BuildRefinementMask(gray, bgGray);

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

    private static double EstimateAverageBackgroundShade(Mat grayImage)
    {
        int s = 5;
        if (grayImage.Width <= s * 2 || grayImage.Height <= s * 2) return 255.0;

        using Mat mTl = new(grayImage, new Rectangle(0, 0, s, s));
        using Mat mTr = new(grayImage, new Rectangle(grayImage.Width - s, 0, s, s));
        using Mat mBl = new(grayImage, new Rectangle(0, grayImage.Height - s, s, s));
        using Mat mBr = new(grayImage, new Rectangle(grayImage.Width - s, grayImage.Height - s, s, s));

        var tl = CvInvoke.Mean(mTl).V0;
        var tr = CvInvoke.Mean(mTr).V0;
        var bl = CvInvoke.Mean(mBl).V0;
        var br = CvInvoke.Mean(mBr).V0;

        return (tl + tr + bl + br) / 4.0;
    }

    private static Mat BuildRefinementMask(Mat grayImage, double bgGray)
    {
        Mat mask = new();
        // Tolerance of 20 shades of grey to catch shadows/gradients in the scanner margin
        double lower = Math.Max(0, bgGray - 20);
        double upper = Math.Min(255, bgGray + 20);

        using ScalarArray lowerArray = new(lower);
        using ScalarArray upperArray = new(upper);
        CvInvoke.InRange(grayImage, lowerArray, upperArray, mask);
        
        // Invert: Photo is white (255), background margin is black (0)
        CvInvoke.BitwiseNot(mask, mask);

        // Remove scanner noise/dust from the black margin
        using Mat openKernel = CvInvoke.GetStructuringElement(MorphShapes.Rectangle, new Size(7, 7), new Point(-1, -1));
        CvInvoke.MorphologyEx(mask, mask, MorphOp.Open, openKernel, new Point(-1, -1), 1, BorderType.Default, new MCvScalar());

        // Solidify the photo area
        using Mat closeKernel = CvInvoke.GetStructuringElement(MorphShapes.Rectangle, new Size(21, 21), new Point(-1, -1));
        CvInvoke.MorphologyEx(mask, mask, MorphOp.Close, closeKernel, new Point(-1, -1), 3, BorderType.Default, new MCvScalar());

        return mask;
    }

    public void ApplyCropToPhoto(int index, Rectangle rect)
    {
        if (index < 0 || index >= DetectedPhotos.Count) return;
        
        Mat photo = DetectedPhotos[index];
        rect.Intersect(new Rectangle(Point.Empty, photo.Size));
        if (rect.Width <= 10 || rect.Height <= 10) return;

        using Mat subMat = new(photo, rect);
        Mat cropped = subMat.Clone();
        DetectedPhotos[index].Dispose();
        DetectedPhotos[index] = cropped;
    }

    public void AddManualCrop(Rectangle rect)
    {
        rect.Intersect(new Rectangle(Point.Empty, Original.Size));
        if (rect.Width <= 10 || rect.Height <= 10) return;

        Rectangle searchRoi = new(rect.X - 20, rect.Y - 20, rect.Width + 40, rect.Height + 40);
        searchRoi.Intersect(new Rectangle(Point.Empty, Original.Size));

        if (searchRoi.Width <= 10 || searchRoi.Height <= 10) return;

        using Mat roiMat = new(Original, searchRoi);
        using Mat roiHsv = new();
        CvInvoke.CvtColor(roiMat, roiHsv, ColorConversion.Bgr2Hsv);
        MCvScalar bgHsv = CustomBackgroundColorHsv ?? SampleBackgroundColor(roiHsv);
        using Mat foreground = new();
        PopulateForegroundMask(roiMat, foreground, bgHsv, BackgroundTolerance, 20, 50);

        using VectorOfVectorOfPoint contours = new();
        CvInvoke.FindContours(foreground, contours, null, RetrType.External, ChainApproxMethod.ChainApproxSimple);

        int bestContourIndex = -1;
        double maxArea = 0;

        for (int i = 0; i < contours.Size; i++)
        {
            double area = CvInvoke.ContourArea(contours[i]);
            if (area > maxArea)
            {
                maxArea = area;
                bestContourIndex = i;
            }
        }

        if (bestContourIndex >= 0)
        {
            using VectorOfPoint bestHull = new();
            CvInvoke.ConvexHull(contours[bestContourIndex], bestHull);

            Point[] points = bestHull.ToArray();
            for (int i = 0; i < points.Length; i++)
            {
                points[i].X += searchRoi.X;
                points[i].Y += searchRoi.Y;
            }
            using VectorOfPoint globalHull = new(points);

            Mat? extracted = null;
            try
            {
                extracted = ExtractPhotoFromContour(globalHull);
                if (!extracted.IsEmpty)
                {
                    DetectedPhotos.Add(extracted);
                    extracted = null; // Transfer ownership
                    return;
                }
            }
            finally
            {
                extracted?.Dispose();
            }
        }

        rect.Intersect(new Rectangle(Point.Empty, Original.Size));
        if (rect.Width > 10 && rect.Height > 10)
        {
            using Mat subMat = new(Original, rect);
            DetectedPhotos.Add(subMat.Clone());
        }
    }

    public void SaveDetectedPhotos(string? customOutputFolder = null, string format = "JPEG", int jpegQuality = 90)
    {
        ArgumentNullException.ThrowIfNull(format);
        string outputFolder;
        if (!string.IsNullOrEmpty(customOutputFolder))
        {
            outputFolder = customOutputFolder;
        }
        else
        {
            string? directory = Path.GetDirectoryName(OriginalFilePath);
            if (string.IsNullOrEmpty(directory)) return;
            outputFolder = Path.Combine(directory, "cropped");
        }

        Directory.CreateDirectory(outputFolder);

        string baseFileName = Path.GetFileNameWithoutExtension(OriginalFilePath);
        string extension = string.Equals(format, "PNG", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";

        int saveCounter = 1;
        for (int i = 0; i < DetectedPhotos.Count; i++)
        {
            if (DetectedPhotos[i].IsEmpty) continue;
            string fileName = Path.Combine(outputFolder, $"{baseFileName}_{saveCounter++}{extension}");
            
            if (string.Equals(format, "PNG", StringComparison.OrdinalIgnoreCase))
            {
                DetectedPhotos[i].Save(fileName);
            }
            else
            {
                KeyValuePair<ImwriteFlags, int>[] parameters = [
                    new KeyValuePair<ImwriteFlags, int>(ImwriteFlags.JpegQuality, jpegQuality)
                ];
                CvInvoke.Imwrite(fileName, DetectedPhotos[i], parameters);
            }
        }
    }
}