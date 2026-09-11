using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using PhotoCropper.Detection;

namespace PhotoCropper.Extraction;

public static class PhotoExtractionEngine
{
    public static PointF[] OrderBoxPoints(PointF[] pts)
    {
        ArgumentNullException.ThrowIfNull(pts);

        var xSorted = pts.OrderBy(p => p.X).ToArray();
        var leftMost = xSorted.Take(2).OrderBy(p => p.Y).ToArray();
        var rightMost = xSorted.Skip(2).OrderBy(p => p.Y).ToArray();

        // [top-left, top-right, bottom-right, bottom-left]
        return [leftMost[0], rightMost[0], rightMost[1], leftMost[1]];
    }

    public static Mat ExtractPhotoFromContour(VectorOfPoint shapeInScanSpace, Mat original, Mat? paddedSource = null, int padOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(shapeInScanSpace);
        ArgumentNullException.ThrowIfNull(original);

        RotatedRect rect = CvInvoke.MinAreaRect(shapeInScanSpace);
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

        // Orientation normalization: default to landscape orientation (standard photo convention)
        if (targetWidth < targetHeight)
        {
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
