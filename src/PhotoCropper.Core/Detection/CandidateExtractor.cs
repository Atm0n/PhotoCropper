using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using PhotoCropper.Core.Extraction;
using PhotoCropper.Core.Models;
using System.Drawing;

namespace PhotoCropper.Core.Detection;

public static class CandidateExtractor
{
    public static IReadOnlyList<CropCandidate> ExtractCandidates(
        Mat foregroundMap,
        int padOffset,
        int originalWidth,
        int originalHeight,
        double minAreaFactor,
        double maxAreaFactor,
        Mat? sourceColorMat = null,
        MCvScalar? backgroundColorBgr = null)
    {
        ArgumentNullException.ThrowIfNull(foregroundMap);

        var result = new List<CropCandidate>();

        using VectorOfVectorOfPoint contours = new();
        CvInvoke.FindContours(foregroundMap, contours, null, RetrType.External, ChainApproxMethod.ChainApproxSimple);

        double totalArea = (double)originalWidth * originalHeight;
        double minArea = totalArea * minAreaFactor;
        double maxArea = totalArea * maxAreaFactor;

        for (int i = 0; i < contours.Size; i++)
        {
            var candidate = ProcessContour(
                contours[i],
                padOffset,
                originalWidth,
                originalHeight,
                minArea,
                maxArea,
                sourceColorMat,
                backgroundColorBgr);

            if (candidate.HasValue)
            {
                result.Add(candidate.Value);
            }
        }

        return result;
    }

    private static CropCandidate? ProcessContour(
        IInputArray contour,
        int padOffset,
        int originalWidth,
        int originalHeight,
        double minArea,
        double maxArea,
        Mat? sourceColorMat,
        MCvScalar? backgroundColorBgr)
    {
        double contourArea = CvInvoke.ContourArea(contour);
        if (contourArea < minArea || contourArea > maxArea) return null;

        using VectorOfPoint hull = new();
        CvInvoke.ConvexHull(contour, hull);
        double hullArea = CvInvoke.ContourArea(hull);

        RotatedRect padRr = CvInvoke.MinAreaRect(hull);
        RotatedRect scanRr = new(new PointF(padRr.Center.X - padOffset, padRr.Center.Y - padOffset), padRr.Size, padRr.Angle);
        RotatedRect rr = PhotoExtractionEngine.RegularizeNearRightAngles(scanRr, 0.35f);

        PointF[] boxPts = rr.GetVertices();
        Point[] shapePoints = new Point[4];
        for (int p = 0; p < 4; p++)
        {
            shapePoints[p] = new Point(
                Math.Clamp((int)Math.Round(boxPts[p].X), 0, originalWidth - 1),
                Math.Clamp((int)Math.Round(boxPts[p].Y), 0, originalHeight - 1)
            );
        }

        using VectorOfPoint tempShape = new(shapePoints);

        float w = rr.Size.Width;
        float h = rr.Size.Height;
        if (w < 20 || h < 20) return null;

        float aspectRatio = Math.Max(w, h) / Math.Max(1.0f, Math.Min(w, h));
        if (aspectRatio > 6.0f) return null;

        double rrArea = Math.Max(1.0, (double)w * h);
        double rectangularity = Math.Clamp(contourArea / rrArea, 0.0, 1.0);
        double convexity = Math.Clamp(contourArea / Math.Max(1.0, hullArea), 0.0, 1.0);

        if (rectangularity < 0.60 || convexity < 0.68) return null;

        if (sourceColorMat != null && !sourceColorMat.IsEmpty && backgroundColorBgr.HasValue)
        {
            Rectangle cBounds = CvInvoke.BoundingRectangle(contour);
            int clampX = Math.Clamp(cBounds.X, 0, sourceColorMat.Width - 1);
            int clampY = Math.Clamp(cBounds.Y, 0, sourceColorMat.Height - 1);
            int clampW = Math.Clamp(cBounds.Width, 1, sourceColorMat.Width - clampX);
            int clampH = Math.Clamp(cBounds.Height, 1, sourceColorMat.Height - clampY);

            if (clampW >= 20 && clampH >= 20)
            {
                using Mat roi = new(sourceColorMat, new Rectangle(clampX, clampY, clampW, clampH));
                MCvScalar mean = new();
                MCvScalar stdDev = new();
                CvInvoke.MeanStdDev(roi, ref mean, ref stdDev);

                double avgStdDev = (stdDev.V0 + stdDev.V1 + stdDev.V2) / 3.0;
                double colorDist = Math.Sqrt(
                    Math.Pow(mean.V0 - backgroundColorBgr.Value.V0, 2) +
                    Math.Pow(mean.V1 - backgroundColorBgr.Value.V1, 2) +
                    Math.Pow(mean.V2 - backgroundColorBgr.Value.V2, 2));

                if (avgStdDev < 6.0 && colorDist < 22.0)
                {
                    return null;
                }
            }
        }

        double quality = Math.Pow(rectangularity, 3) * Math.Pow(convexity, 2);
        double score = contourArea * quality;

        return new CropCandidate
        {
            ShapePoints = shapePoints,
            Rect = CvInvoke.BoundingRectangle(tempShape),
            Score = score,
            Rotated = rr,
            Area = contourArea,
            Rectangularity = rectangularity,
            Convexity = convexity
        };
    }
}
