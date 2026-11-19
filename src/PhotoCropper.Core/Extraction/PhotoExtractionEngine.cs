using OpenCvSharp;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Detection;
using PhotoCropper.Core.Models;

namespace PhotoCropper.Core.Extraction;

public static class PhotoExtractionEngine
{
    /// <inheritdoc cref="OrderBoxPoints(Point2f[])"/>
    public static System.Drawing.PointF[] OrderBoxPoints(System.Drawing.PointF[] pts)
    {
        ArgumentNullException.ThrowIfNull(pts);
        return [.. OrderBoxPoints(CvInterop.ToCv(pts)).Select(CvInterop.ToPointF)];
    }

    /// <summary>Orders a box's corners as top-left, top-right, bottom-right, bottom-left.</summary>
    public static Point2f[] OrderBoxPoints(Point2f[] pts)
    {
        ArgumentNullException.ThrowIfNull(pts);
        if (pts.Length != 4) return pts;

        float cx = pts.Average(p => p.X);
        float cy = pts.Average(p => p.Y);

        // Sort points by polar angle from center in clockwise order
        var sorted = pts.OrderBy(p => Math.Atan2(p.Y - cy, p.X - cx)).ToArray();

        // Top-left candidate has the smallest (X + Y)
        int bestTlIdx = 0;
        double minSum = double.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            double sum = sorted[i].X + sorted[i].Y;
            if (sum < minSum)
            {
                minSum = sum;
                bestTlIdx = i;
            }
        }

        Point2f tl = sorted[bestTlIdx];
        Point2f tr = sorted[(bestTlIdx + 1) % 4];
        Point2f br = sorted[(bestTlIdx + 2) % 4];
        Point2f bl = sorted[(bestTlIdx + 3) % 4];

        // Ensure tl -> tr corresponds to the mostly-horizontal edge (angle within [-45, 45] deg)
        double dx = tr.X - tl.X;
        double dy = tr.Y - tl.Y;
        double deg = Math.Atan2(dy, dx) * (180.0 / Math.PI);

        if (deg > 45.0 && deg <= 135.0)
        {
            return [bl, tl, tr, br];
        }
        if (deg < -45.0 && deg >= -135.0)
        {
            return [tr, br, bl, tl];
        }

        return [tl, tr, br, bl];
    }

    public static RotatedRect RegularizeNearRightAngles(RotatedRect rect, float toleranceDegrees = 1.5f)
    {
        Point2f[] pts = rect.Points();
        if (pts.Length < 4) return rect;

        double dx1 = pts[1].X - pts[0].X;
        double dy1 = pts[1].Y - pts[0].Y;
        double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);

        double dx2 = pts[2].X - pts[1].X;
        double dy2 = pts[2].Y - pts[1].Y;
        double len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);

        if (len1 <= 1.0 || len2 <= 1.0) return rect;

        double theta1 = Math.Atan2(dy1, dx1) * (180.0 / Math.PI);
        double normAngle = theta1;
        while (normAngle > 45.0) normAngle -= 90.0;
        while (normAngle < -45.0) normAngle += 90.0;

        if (Math.Abs(normAngle) <= toleranceDegrees)
        {
            double rad1 = theta1 * (Math.PI / 180.0);
            float hWidth, vHeight;
            if (Math.Abs(Math.Cos(rad1)) >= Math.Abs(Math.Sin(rad1)))
            {
                hWidth = (float)len1;
                vHeight = (float)len2;
            }
            else
            {
                hWidth = (float)len2;
                vHeight = (float)len1;
            }

            return new RotatedRect(rect.Center, new Size2f(hWidth, vHeight), 0f);
        }

        return rect;
    }

    public static Mat ExtractPhotoFromContour(Point[] shapeInScanSpace, Mat original, Mat? paddedSource = null, int padOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(shapeInScanSpace);
        ArgumentNullException.ThrowIfNull(original);

        RotatedRect rawRect = Cv2.MinAreaRect(shapeInScanSpace);
        RotatedRect rect = RegularizeNearRightAngles(rawRect);
        return ExtractPhotoFromRotatedRect(rect, original, paddedSource, padOffset);
    }

    public static Mat ExtractPhotoFromRotatedRect(RotatedRect rect, Mat original, Mat? paddedSource = null, int padOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (original.Empty() || original.Width <= 0 || original.Height <= 0) return new Mat();

        Point2f[] srcPoints = OrderBoxPoints(rect.Points());

        // If extracting from the padded source, shift the crop quad coordinates to padded space
        if (paddedSource != null && padOffset > 0)
        {
            for (int i = 0; i < srcPoints.Length; i++)
            {
                srcPoints[i] = new Point2f(srcPoints[i].X + padOffset, srcPoints[i].Y + padOffset);
            }
        }

        Mat extractSource = paddedSource ?? original;

        float widthA = (float)Math.Sqrt(Math.Pow(srcPoints[1].X - srcPoints[0].X, 2) + Math.Pow(srcPoints[1].Y - srcPoints[0].Y, 2));
        float widthB = (float)Math.Sqrt(Math.Pow(srcPoints[2].X - srcPoints[3].X, 2) + Math.Pow(srcPoints[2].Y - srcPoints[3].Y, 2));
        int targetWidth = (int)Math.Round(Math.Max(widthA, widthB));

        float heightA = (float)Math.Sqrt(Math.Pow(srcPoints[3].X - srcPoints[0].X, 2) + Math.Pow(srcPoints[3].Y - srcPoints[0].Y, 2));
        float heightB = (float)Math.Sqrt(Math.Pow(srcPoints[2].X - srcPoints[1].X, 2) + Math.Pow(srcPoints[2].Y - srcPoints[1].Y, 2));
        int targetHeight = (int)Math.Round(Math.Max(heightA, heightB));

        if (targetWidth <= 10 || targetHeight <= 10) return new Mat();

        Point2f[] dstPoints =
        [
            new Point2f(0, 0),
            new Point2f(targetWidth - 1, 0),
            new Point2f(targetWidth - 1, targetHeight - 1),
            new Point2f(0, targetHeight - 1)
        ];

        // Crop tightly around the candidate quad with a margin to avoid converting and transforming the entire scan
        float minX = Math.Max(0, Math.Min(Math.Min(srcPoints[0].X, srcPoints[1].X), Math.Min(srcPoints[2].X, srcPoints[3].X)) - 4);
        float minY = Math.Max(0, Math.Min(Math.Min(srcPoints[0].Y, srcPoints[1].Y), Math.Min(srcPoints[2].Y, srcPoints[3].Y)) - 4);
        float maxX = Math.Min(extractSource.Width, Math.Max(Math.Max(srcPoints[0].X, srcPoints[1].X), Math.Max(srcPoints[2].X, srcPoints[3].X)) + 4);
        float maxY = Math.Min(extractSource.Height, Math.Max(Math.Max(srcPoints[0].Y, srcPoints[1].Y), Math.Max(srcPoints[2].Y, srcPoints[3].Y)) + 4);

        Rect roi = new((int)minX, (int)minY, (int)Math.Ceiling(maxX - minX), (int)Math.Ceiling(maxY - minY));
        roi = roi.Intersect(new Rect(0, 0, extractSource.Width, extractSource.Height));

        if (roi.Width <= 10 || roi.Height <= 10) return new Mat();

        Point2f[] roiSrcPoints =
        [
            new Point2f(srcPoints[0].X - roi.X, srcPoints[0].Y - roi.Y),
            new Point2f(srcPoints[1].X - roi.X, srcPoints[1].Y - roi.Y),
            new Point2f(srcPoints[2].X - roi.X, srcPoints[2].Y - roi.Y),
            new Point2f(srcPoints[3].X - roi.X, srcPoints[3].Y - roi.Y)
        ];

        using Mat roiMat = new(extractSource, roi);
        using Mat bgraRoi = new();
        if (roiMat.Channels() == 1)
        {
            Cv2.CvtColor(roiMat, bgraRoi, ColorConversionCodes.GRAY2BGRA);
        }
        else if (roiMat.Channels() == 3)
        {
            Cv2.CvtColor(roiMat, bgraRoi, ColorConversionCodes.BGR2BGRA);
        }
        else
        {
            roiMat.CopyTo(bgraRoi);
        }

        using Mat perspectiveMatrix = Cv2.GetPerspectiveTransform(roiSrcPoints, dstPoints);
        Mat result = new();
        // Transparent border: Scalar(0, 0, 0, 0) for alpha channel
        Cv2.WarpPerspective(bgraRoi, result, perspectiveMatrix, new Size(targetWidth, targetHeight), InterpolationFlags.Cubic, BorderTypes.Constant, new Scalar(0, 0, 0, 0));

        return result;
    }

    public static (Mat Extracted, CropCandidate? Candidate) ExtractManualCrop(
        Mat original,
        Rect rect,
        Scalar? customBgHsv,
        double bgTolerance,
        double cannyLow,
        double cannyHigh)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (original.Empty() || original.Width <= 0 || original.Height <= 0) return (new Mat(), null);

        // OpenCvSharp's Intersect returns the intersection (System.Drawing's changed the rectangle in place).
        var bounds = new Rect(0, 0, original.Width, original.Height);
        rect = rect.Intersect(bounds);
        if (rect.Width <= 10 || rect.Height <= 10) return (new Mat(), null);

        Rect searchRoi = new Rect(rect.X - 20, rect.Y - 20, rect.Width + 40, rect.Height + 40).Intersect(bounds);

        if (searchRoi.Width > 10 && searchRoi.Height > 10)
        {
            using Mat roiMat = new(original, searchRoi);
            using Mat bgrRoi = PhotoCropperEngine.NormalizeToBgr(roiMat);
            using Mat roiHsv = new();
            Cv2.CvtColor(bgrRoi, roiHsv, ColorConversionCodes.BGR2HSV);
            Scalar bgHsv = customBgHsv ?? BackgroundAnalyzer.SampleBackgroundColor(roiHsv);

            using Mat foreground = new();
            ForegroundMaskGenerator.PopulateForegroundMask(bgrRoi, foreground, bgHsv, bgTolerance, cannyLow, cannyHigh);

            Cv2.FindContours(foreground, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            int bestContourIndex = -1;
            double maxArea = 0;

            for (int i = 0; i < contours.Length; i++)
            {
                double area = Cv2.ContourArea(contours[i]);
                if (area > maxArea)
                {
                    maxArea = area;
                    bestContourIndex = i;
                }
            }

            if (bestContourIndex >= 0)
            {
                Point[] globalHull = Cv2.ConvexHull(contours[bestContourIndex]);
                for (int i = 0; i < globalHull.Length; i++)
                {
                    globalHull[i].X += searchRoi.X;
                    globalHull[i].Y += searchRoi.Y;
                }

                Mat extracted = ExtractPhotoFromContour(globalHull, original);
                if (!extracted.Empty())
                {
                    // Use the same straightened box the photo was extracted from, so re-extracting the
                    // candidate (session restore, export) reproduces this exact photo.
                    var minRect = RegularizeNearRightAngles(Cv2.MinAreaRect(globalHull));
                    var rectBound = Cv2.BoundingRect(globalHull);
                    var cand = new CropCandidate(
                        CvInterop.ToPoints(globalHull),
                        CvInterop.ToRectangle(rectBound),
                        9999.0,
                        CvInterop.ToBox(minRect),
                        Cv2.ContourArea(globalHull),
                        1.0, 1.0);
                    return (extracted, cand);
                }
                extracted.Dispose();
            }
        }

        using Mat subMat = new(original, rect);
        var exactMinRect = new RotatedRect(
            new Point2f(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f),
            new Size2f(rect.Width, rect.Height),
            0f);
        var exactCand = new CropCandidate(
            [
                new System.Drawing.Point(rect.X, rect.Y),
                new System.Drawing.Point(rect.Right, rect.Y),
                new System.Drawing.Point(rect.Right, rect.Bottom),
                new System.Drawing.Point(rect.X, rect.Bottom)
            ],
            CvInterop.ToRectangle(rect),
            5000.0, CvInterop.ToBox(exactMinRect),
            rect.Width * rect.Height,
            1.0, 1.0);
        return (subMat.Clone(), exactCand);
    }
}
