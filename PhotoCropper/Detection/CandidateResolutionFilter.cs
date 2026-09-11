using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using PhotoCropper.Models;

namespace PhotoCropper.Detection;

public static class CandidateResolutionFilter
{
    public static double CalculatePolygonIntersectionArea(Point[] poly1, Rectangle bounds1, Point[] poly2, Rectangle bounds2)
    {
        ArgumentNullException.ThrowIfNull(poly1);
        ArgumentNullException.ThrowIfNull(poly2);

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

    public static IReadOnlyList<CropCandidate> FilterCandidates(IReadOnlyList<CropCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0) return [];

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
                if (child.Area >= parent.Area * 0.85 || child.Area < parent.Area * 0.15) continue;
                if (child.Rectangularity < 0.65) continue;

                double overlap = CalculatePolygonIntersectionArea(child.ShapePoints, child.Rect, parent.ShapePoints, parent.Rect);
                // Child is mostly contained inside parent
                if (overlap / child.Area >= 0.70)
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
                        double subOverlap = CalculatePolygonIntersectionArea(candA.ShapePoints, candA.Rect, candB.ShapePoints, candB.Rect);
                        double minSubArea = Math.Min(candA.Area, candB.Area);

                        // If two sub-candidates don't heavily overlap each other and their combined area accounts for > 45% of parent
                        if (subOverlap / minSubArea < 0.25 && (candA.Area + candB.Area) >= parent.Area * 0.45)
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
            .OrderByDescending(c => c.Score)
            .ToList();

        var acceptedCandidates = new List<CropCandidate>();
        var acceptedPolys = new List<VectorOfPoint>();

        try
        {
            foreach (var cand in validCandidates)
            {
                Point center = new(cand.Rect.X + cand.Rect.Width / 2, cand.Rect.Y + cand.Rect.Height / 2);
                
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
                using VectorOfPoint shape = new(cand.ShapePoints);

                foreach (var accepted in acceptedPolys)
                {
                    Rectangle accBounds = CvInvoke.BoundingRectangle(accepted);
                    double overlapPixels = CalculatePolygonIntersectionArea(cand.ShapePoints, cand.Rect, accepted.ToArray(), accBounds);
                    double minPolyArea = Math.Min(CvInvoke.ContourArea(shape), CvInvoke.ContourArea(accepted));

                    // If overlap exceeds 15% of the smaller photo, reject the duplicate/invading candidate
                    if (minPolyArea > 0 && (overlapPixels / minPolyArea) > 0.15)
                    {
                        excessiveOverlap = true;
                        break;
                    }
                }
                if (excessiveOverlap) continue;
                
                acceptedCandidates.Add(cand);
                acceptedPolys.Add(new VectorOfPoint(cand.ShapePoints));
            }

            return acceptedCandidates;
        }
        finally
        {
            foreach (var poly in acceptedPolys)
            {
                poly.Dispose();
            }
        }
    }
}
