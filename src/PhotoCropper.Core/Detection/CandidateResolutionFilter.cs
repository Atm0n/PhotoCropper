using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using PhotoCropper.Core.Models;
using System.Drawing;

namespace PhotoCropper.Core.Detection;

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

        Point[] shifted1 = [.. poly1.Select(p => new Point(p.X - intersectBox.X, p.Y - intersectBox.Y))];
        Point[] shifted2 = [.. poly2.Select(p => new Point(p.X - intersectBox.X, p.Y - intersectBox.Y))];

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

        var compositeIndices = FindCompositeIndices(candidates);

        var validCandidates = candidates
            .Where((_, idx) => !compositeIndices.Contains(idx))
            .OrderByDescending(c => c.Score)
            .ToList();

        return FilterOverlappingDuplicates(validCandidates);
    }

    private static HashSet<int> FindCompositeIndices(IReadOnlyList<CropCandidate> candidates)
    {
        var compositeIndices = new HashSet<int>();
        for (int i = 0; i < candidates.Count; i++)
        {
            var parent = candidates[i];
            var subCandidates = new List<int>();

            for (int j = 0; j < candidates.Count; j++)
            {
                if (i == j) continue;
                var child = candidates[j];

                if (child.Area >= parent.Area * 0.85 || child.Area < parent.Area * 0.02) continue;
                if (child.Rectangularity < 0.60) continue;

                double overlap = CalculatePolygonIntersectionArea(child.ShapePoints, child.Rect, parent.ShapePoints, parent.Rect);
                if (overlap / child.Area >= 0.70)
                {
                    subCandidates.Add(j);
                }
            }

            if (subCandidates.Count >= 2)
            {
                if (HasDisjointSubCandidates(candidates, subCandidates))
                {
                    compositeIndices.Add(i);
                }
            }
            else if (subCandidates.Count == 1 && parent.Area >= candidates.Max(c => c.Area) * 0.95 && parent.Area > (double)parent.Rect.Width * parent.Rect.Height * 0.70)
            {
                compositeIndices.Add(i);
            }
        }
        return compositeIndices;
    }

    private static bool HasDisjointSubCandidates(IReadOnlyList<CropCandidate> candidates, List<int> subCandidates)
    {
        for (int a = 0; a < subCandidates.Count; a++)
        {
            var candA = candidates[subCandidates[a]];
            for (int b = a + 1; b < subCandidates.Count; b++)
            {
                var candB = candidates[subCandidates[b]];
                double subOverlap = CalculatePolygonIntersectionArea(candA.ShapePoints, candA.Rect, candB.ShapePoints, candB.Rect);
                double minSubArea = Math.Min(candA.Area, candB.Area);

                if (minSubArea > 0 && subOverlap / minSubArea < 0.30)
                {
                    return true;
                }
            }
        }
        return false;
    }


    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope")]
    private static List<CropCandidate> FilterOverlappingDuplicates(List<CropCandidate> validCandidates)
    {
        var acceptedCandidates = new List<CropCandidate>();
        var acceptedPolys = new List<VectorOfPoint>();

        try
        {
            foreach (var cand in validCandidates)
            {
                if (IsCenterInsideAccepted(cand, acceptedPolys)) continue;

                using VectorOfPoint shape = new(cand.ShapePoints);

                if (HasSignificantOverlapWithAccepted(cand, shape, acceptedPolys)) continue;

                acceptedCandidates.Add(cand);

                acceptedPolys.Add(new VectorOfPoint(cand.ShapePoints));
            }
        }
        finally
        {
            foreach (var poly in acceptedPolys)
            {
                poly.Dispose();
            }
        }

        return acceptedCandidates;
    }

    private static bool IsCenterInsideAccepted(CropCandidate cand, List<VectorOfPoint> acceptedPolys)
    {
        Point center = new(cand.Rect.X + cand.Rect.Width / 2, cand.Rect.Y + cand.Rect.Height / 2);
        foreach (var accepted in acceptedPolys)
        {
            if (CvInvoke.PointPolygonTest(accepted, center, false) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasSignificantOverlapWithAccepted(CropCandidate cand, VectorOfPoint shape, List<VectorOfPoint> acceptedPolys)
    {
        foreach (var accepted in acceptedPolys)
        {
            double overlap = CalculatePolygonIntersectionArea(cand.ShapePoints, cand.Rect, accepted.ToArray(), CvInvoke.BoundingRectangle(accepted));
            double minPolyArea = Math.Min(CvInvoke.ContourArea(shape), CvInvoke.ContourArea(accepted));
            if (minPolyArea > 0 && overlap / minPolyArea > 0.35)
            {
                return true;
            }
        }
        return false;
    }
}
