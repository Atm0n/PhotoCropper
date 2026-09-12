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
        double maxAreaFactor)
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

            // Map coordinates back from padded space to original image space
            Point[] shapePoints = new Point[rawPoints.Length];
            for (int p = 0; p < rawPoints.Length; p++)
            {
                shapePoints[p] = new Point(
                    Math.Clamp(rawPoints[p].X - padOffset, 0, originalWidth - 1),
                    Math.Clamp(rawPoints[p].Y - padOffset, 0, originalHeight - 1)
                );
            }

            using VectorOfPoint tempShape = new(shapePoints);
            RotatedRect rr = PhotoExtractionEngine.RegularizeNearRightAngles(CvInvoke.MinAreaRect(tempShape));

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

            result.Add(new CropCandidate(
                shapePoints,
                CvInvoke.BoundingRectangle(tempShape),
                score,
                rr,
                contourArea,
                rectangularity,
                convexity));
        }

        return result;
    }
}
