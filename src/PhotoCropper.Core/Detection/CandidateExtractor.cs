using OpenCvSharp;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Extraction;
using PhotoCropper.Core.Models;

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
        Scalar? backgroundColorBgr = null)
    {
        ArgumentNullException.ThrowIfNull(foregroundMap);

        var result = new List<CropCandidate>();

        Cv2.FindContours(foregroundMap, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        double totalArea = (double)originalWidth * originalHeight;
        double minArea = totalArea * minAreaFactor;
        double maxArea = totalArea * maxAreaFactor;

        for (int i = 0; i < contours.Length; i++)
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
        Point[] contour,
        int padOffset,
        int originalWidth,
        int originalHeight,
        double minArea,
        double maxArea,
        Mat? sourceColorMat,
        Scalar? backgroundColorBgr)
    {
        double contourArea = Cv2.ContourArea(contour);
        if (contourArea < minArea || contourArea > maxArea) return null;

        Point[] hull = Cv2.ConvexHull(contour);
        double hullArea = Cv2.ContourArea(hull);

        RotatedRect padRr = Cv2.MinAreaRect(hull);
        RotatedRect scanRr = new(new Point2f(padRr.Center.X - padOffset, padRr.Center.Y - padOffset), padRr.Size, padRr.Angle);
        RotatedRect rr = PhotoExtractionEngine.RegularizeNearRightAngles(scanRr, 0.35f);

        Point2f[] boxPts = rr.Points();
        Point[] shapePoints = new Point[4];
        for (int p = 0; p < 4; p++)
        {
            shapePoints[p] = new Point(
                Math.Clamp((int)Math.Round(boxPts[p].X), 0, originalWidth - 1),
                Math.Clamp((int)Math.Round(boxPts[p].Y), 0, originalHeight - 1)
            );
        }

        float w = rr.Size.Width;
        float h = rr.Size.Height;
        if (w < 20 || h < 20) return null;

        float aspectRatio = Math.Max(w, h) / Math.Max(1.0f, Math.Min(w, h));
        if (aspectRatio > 6.0f) return null;

        double rrArea = Math.Max(1.0, (double)w * h);
        double rectangularity = Math.Clamp(contourArea / rrArea, 0.0, 1.0);
        double convexity = Math.Clamp(contourArea / Math.Max(1.0, hullArea), 0.0, 1.0);

        if (rectangularity < 0.60 || convexity < 0.68) return null;

        if (sourceColorMat != null && !sourceColorMat.Empty() && backgroundColorBgr.HasValue)
        {
            Rect cBounds = Cv2.BoundingRect(contour);
            int clampX = Math.Clamp(cBounds.X, 0, sourceColorMat.Width - 1);
            int clampY = Math.Clamp(cBounds.Y, 0, sourceColorMat.Height - 1);
            int clampW = Math.Clamp(cBounds.Width, 1, sourceColorMat.Width - clampX);
            int clampH = Math.Clamp(cBounds.Height, 1, sourceColorMat.Height - clampY);

            if (clampW >= 20 && clampH >= 20)
            {
                using Mat roi = new(sourceColorMat, new Rect(clampX, clampY, clampW, clampH));
                Cv2.MeanStdDev(roi, out Scalar mean, out Scalar stdDev);

                double avgStdDev = (stdDev.Val0 + stdDev.Val1 + stdDev.Val2) / 3.0;
                double colorDist = Math.Sqrt(
                    Math.Pow(mean.Val0 - backgroundColorBgr.Value.Val0, 2) +
                    Math.Pow(mean.Val1 - backgroundColorBgr.Value.Val1, 2) +
                    Math.Pow(mean.Val2 - backgroundColorBgr.Value.Val2, 2));

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
            ShapePoints = CvInterop.ToPoints(shapePoints),
            Rect = CvInterop.ToRectangle(Cv2.BoundingRect(shapePoints)),
            Score = score,
            Rotated = CvInterop.ToBox(rr),
            Area = contourArea,
            Rectangularity = rectangularity,
            Convexity = convexity
        };
    }
}
