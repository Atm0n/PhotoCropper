using OpenCvSharp;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Models;

namespace PhotoCropper.Core.Detection;

public static class CandidateResolutionFilter
{
    public static double CalculatePolygonIntersectionArea(Point[] poly1, Rect bounds1, Point[] poly2, Rect bounds2)
    {
        ArgumentNullException.ThrowIfNull(poly1);
        ArgumentNullException.ThrowIfNull(poly2);

        Rect intersectBox = Rect.Intersect(bounds1, bounds2);
        if (intersectBox.Width <= 0 || intersectBox.Height <= 0)
        {
            return 0;
        }

        using Mat mask1 = new(intersectBox.Size, MatType.CV_8UC1);
        using Mat mask2 = new(intersectBox.Size, MatType.CV_8UC1);
        using Mat maskOverlap = new();
        mask1.SetTo(new Scalar(0));
        mask2.SetTo(new Scalar(0));

        Point[] shifted1 = [.. poly1.Select(p => new Point(p.X - intersectBox.X, p.Y - intersectBox.Y))];
        Point[] shifted2 = [.. poly2.Select(p => new Point(p.X - intersectBox.X, p.Y - intersectBox.Y))];

        Cv2.FillPoly(mask1, [shifted1], new Scalar(255));
        Cv2.FillPoly(mask2, [shifted2], new Scalar(255));

        Cv2.BitwiseAnd(mask1, mask2, maskOverlap);
        return Cv2.CountNonZero(maskOverlap);
    }

    /// <summary>The overlap of two candidates' shapes, in pixels.</summary>
    private static double Overlap(CropCandidate a, CropCandidate b) =>
        CalculatePolygonIntersectionArea(CvInterop.ToCv(a.ShapePoints), CvInterop.ToCv(a.Rect), CvInterop.ToCv(b.ShapePoints), CvInterop.ToCv(b.Rect));

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

                double overlap = Overlap(child, parent);
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
                double subOverlap = Overlap(candA, candB);
                double minSubArea = Math.Min(candA.Area, candB.Area);

                if (minSubArea > 0 && subOverlap / minSubArea < 0.30)
                {
                    return true;
                }
            }
        }
        return false;
    }


    private static List<CropCandidate> FilterOverlappingDuplicates(List<CropCandidate> validCandidates)
    {
        var acceptedCandidates = new List<CropCandidate>();
        var acceptedPolys = new List<Point[]>();

        foreach (var cand in validCandidates)
        {
            if (IsCenterInsideAccepted(cand, acceptedPolys)) continue;

            Point[] shape = CvInterop.ToCv(cand.ShapePoints);
            if (HasSignificantOverlapWithAccepted(cand, shape, acceptedPolys)) continue;

            acceptedCandidates.Add(cand);
            acceptedPolys.Add(shape);
        }

        return acceptedCandidates;
    }

    private static bool IsCenterInsideAccepted(CropCandidate cand, List<Point[]> acceptedPolys)
    {
        Point2f center = new(cand.Rect.X + cand.Rect.Width / 2, cand.Rect.Y + cand.Rect.Height / 2);
        foreach (var accepted in acceptedPolys)
        {
            if (Cv2.PointPolygonTest(accepted, center, false) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasSignificantOverlapWithAccepted(CropCandidate cand, Point[] shape, List<Point[]> acceptedPolys)
    {
        foreach (var accepted in acceptedPolys)
        {
            double overlap = CalculatePolygonIntersectionArea(shape, CvInterop.ToCv(cand.Rect), accepted, Cv2.BoundingRect(accepted));
            double minPolyArea = Math.Min(Cv2.ContourArea(shape), Cv2.ContourArea(accepted));
            if (minPolyArea > 0 && overlap / minPolyArea > 0.35)
            {
                return true;
            }
        }
        return false;
    }
}
