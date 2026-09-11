using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using PhotoCropper.Detection;
using System.Drawing;

namespace PhotoCropper.Extraction;

public static class PhotoExtractionEngine
{
    public static PointF[] OrderBoxPoints(PointF[] pts)
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

        PointF tl = sorted[bestTlIdx];
        PointF tr = sorted[(bestTlIdx + 1) % 4];
        PointF br = sorted[(bestTlIdx + 2) % 4];
        PointF bl = sorted[(bestTlIdx + 3) % 4];

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
        PointF[] pts = rect.GetVertices();
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

            return new RotatedRect(rect.Center, new SizeF(hWidth, vHeight), 0f);
        }

        return rect;
    }

    public static Mat ExtractPhotoFromContour(VectorOfPoint shapeInScanSpace, Mat original, Mat? paddedSource = null, int padOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(shapeInScanSpace);
        ArgumentNullException.ThrowIfNull(original);

        RotatedRect rawRect = CvInvoke.MinAreaRect(shapeInScanSpace);
        RotatedRect rect = RegularizeNearRightAngles(rawRect);
        PointF[] srcPoints = OrderBoxPoints(rect.GetVertices());

        // If extracting from the padded source, shift the crop quad coordinates to padded space
        if (paddedSource != null && padOffset > 0)
        {
            for (int i = 0; i < srcPoints.Length; i++)
            {
                srcPoints[i] = new PointF(srcPoints[i].X + padOffset, srcPoints[i].Y + padOffset);
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

        PointF[] dstPoints =
        [
            new PointF(0, 0),
            new PointF(targetWidth - 1, 0),
            new PointF(targetWidth - 1, targetHeight - 1),
            new PointF(0, targetHeight - 1)
        ];

        // Crop tightly around the candidate quad with a margin to avoid converting and transforming the entire scan
        float minX = Math.Max(0, Math.Min(Math.Min(srcPoints[0].X, srcPoints[1].X), Math.Min(srcPoints[2].X, srcPoints[3].X)) - 4);
        float minY = Math.Max(0, Math.Min(Math.Min(srcPoints[0].Y, srcPoints[1].Y), Math.Min(srcPoints[2].Y, srcPoints[3].Y)) - 4);
        float maxX = Math.Min(extractSource.Width, Math.Max(Math.Max(srcPoints[0].X, srcPoints[1].X), Math.Max(srcPoints[2].X, srcPoints[3].X)) + 4);
        float maxY = Math.Min(extractSource.Height, Math.Max(Math.Max(srcPoints[0].Y, srcPoints[1].Y), Math.Max(srcPoints[2].Y, srcPoints[3].Y)) + 4);

        Rectangle roi = new((int)minX, (int)minY, (int)Math.Ceiling(maxX - minX), (int)Math.Ceiling(maxY - minY));
        roi.Intersect(new Rectangle(0, 0, extractSource.Width, extractSource.Height));

        if (roi.Width <= 10 || roi.Height <= 10) return new Mat();

        PointF[] roiSrcPoints =
        [
            new PointF(srcPoints[0].X - roi.X, srcPoints[0].Y - roi.Y),
            new PointF(srcPoints[1].X - roi.X, srcPoints[1].Y - roi.Y),
            new PointF(srcPoints[2].X - roi.X, srcPoints[2].Y - roi.Y),
            new PointF(srcPoints[3].X - roi.X, srcPoints[3].Y - roi.Y)
        ];

        using Mat roiMat = new(extractSource, roi);
        using Mat bgraRoi = new();
        if (roiMat.NumberOfChannels == 3)
        {
            CvInvoke.CvtColor(roiMat, bgraRoi, ColorConversion.Bgr2Bgra);
        }
        else
        {
            roiMat.CopyTo(bgraRoi);
        }

        using Mat perspectiveMatrix = CvInvoke.GetPerspectiveTransform(roiSrcPoints, dstPoints);
        Mat result = new();
        // Transparent border: MCvScalar(0, 0, 0, 0) for alpha channel
        CvInvoke.WarpPerspective(bgraRoi, result, perspectiveMatrix, new Size(targetWidth, targetHeight), Inter.Cubic, Warp.Default, BorderType.Constant, new MCvScalar(0, 0, 0, 0));

        return result;
    }

    public static Mat ExtractManualCrop(
        Mat original,
        Rectangle rect,
        MCvScalar? customBgHsv,
        double bgTolerance,
        double cannyLow,
        double cannyHigh)
    {
        ArgumentNullException.ThrowIfNull(original);

        rect.Intersect(new Rectangle(Point.Empty, original.Size));
        if (rect.Width <= 10 || rect.Height <= 10) return new Mat();

        Rectangle searchRoi = new(rect.X - 20, rect.Y - 20, rect.Width + 40, rect.Height + 40);
        searchRoi.Intersect(new Rectangle(Point.Empty, original.Size));

        if (searchRoi.Width > 10 && searchRoi.Height > 10)
        {
            using Mat roiMat = new(original, searchRoi);
            using Mat roiHsv = new();
            CvInvoke.CvtColor(roiMat, roiHsv, ColorConversion.Bgr2Hsv);
            MCvScalar bgHsv = customBgHsv ?? BackgroundAnalyzer.SampleBackgroundColor(roiHsv);

            using Mat foreground = new();
            ForegroundMaskGenerator.PopulateForegroundMask(roiMat, foreground, bgHsv, bgTolerance, cannyLow, cannyHigh);

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

                Mat extracted = ExtractPhotoFromContour(globalHull, original);
                if (!extracted.IsEmpty)
                {
                    return extracted;
                }
                extracted.Dispose();
            }
        }

        using Mat subMat = new(original, rect);
        return subMat.Clone();
    }
}
