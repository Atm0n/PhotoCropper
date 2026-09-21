using Emgu.CV.Structure;
using PhotoCropper.Core.Detection;
using PhotoCropper.Core.Models;
using System.Drawing;

namespace PhotoCropper.Core.Tests.Detection;

public sealed class CandidateResolutionFilterTests
{
    [Fact]
    public void FilterCandidates_ShouldDiscardCompositeCandidateInFavorOfChildren()
    {
        // Parent candidate spanning 1000x500
        Point[] parentPts = [new Point(0, 0), new Point(1000, 0), new Point(1000, 500), new Point(0, 500)];
        var parent = new CropCandidate(parentPts, new Rectangle(0, 0, 1000, 500), 500000, new RotatedRect(new PointF(500, 250), new SizeF(1000, 500), 0), 500000, 0.9, 0.9);

        // Child 1 spanning (0, 0, 480, 500)
        Point[] child1Pts = [new Point(0, 0), new Point(480, 0), new Point(480, 500), new Point(0, 500)];
        var child1 = new CropCandidate(child1Pts, new Rectangle(0, 0, 480, 500), 240000, new RotatedRect(new PointF(240, 250), new SizeF(480, 500), 0), 240000, 0.95, 0.99);

        // Child 2 spanning (520, 0, 480, 500)
        Point[] child2Pts = [new Point(520, 0), new Point(1000, 0), new Point(1000, 500), new Point(520, 500)];
        var child2 = new CropCandidate(child2Pts, new Rectangle(520, 0, 480, 500), 240000, new RotatedRect(new PointF(760, 250), new SizeF(480, 500), 0), 240000, 0.95, 0.99);

        var filtered = CandidateResolutionFilter.FilterCandidates([parent, child1, child2]);

        // Parent should be rejected as composite, retaining child 1 and child 2
        filtered.Count.ShouldBe(2);
        filtered.ShouldNotContain(parent);
    }

    [Fact]
    public void FilterCandidates_ShouldPreserveSeparatedCandidates()
    {
        Point[] pts1 = [new Point(0, 0), new Point(400, 0), new Point(400, 400), new Point(0, 400)];
        var cand1 = new CropCandidate(pts1, new Rectangle(0, 0, 400, 400), 160000, new RotatedRect(new PointF(200, 200), new SizeF(400, 400), 0), 160000, 0.98, 0.99);

        Point[] pts2 = [new Point(500, 0), new Point(900, 0), new Point(900, 400), new Point(500, 400)];
        var cand2 = new CropCandidate(pts2, new Rectangle(500, 0, 400, 400), 160000, new RotatedRect(new PointF(700, 200), new SizeF(400, 400), 0), 160000, 0.98, 0.99);

        var filtered = CandidateResolutionFilter.FilterCandidates([cand1, cand2]);
        filtered.Count.ShouldBe(2);
    }

    [Fact]
    public void CalculatePolygonIntersectionArea_DisjointRects_ReturnsZero()
    {
        Point[] pts1 = [new Point(0, 0), new Point(100, 0), new Point(100, 100), new Point(0, 100)];
        Point[] pts2 = [new Point(200, 200), new Point(300, 200), new Point(300, 300), new Point(200, 300)];

        double overlap = CandidateResolutionFilter.CalculatePolygonIntersectionArea(
            pts1, new Rectangle(0, 0, 100, 100),
            pts2, new Rectangle(200, 200, 100, 100));

        overlap.ShouldBe(0);
    }

    [Fact]
    public void CalculatePolygonIntersectionArea_OverlappingRects_ReturnsAccurateArea()
    {
        Point[] pts1 = [new Point(0, 0), new Point(100, 0), new Point(100, 100), new Point(0, 100)];
        Point[] pts2 = [new Point(50, 50), new Point(150, 50), new Point(150, 150), new Point(50, 150)];

        // Overlap should be 50x50 = 2500
        double overlap = CandidateResolutionFilter.CalculatePolygonIntersectionArea(
            pts1, new Rectangle(0, 0, 100, 100),
            pts2, new Rectangle(50, 50, 100, 100));

        overlap.ShouldBeInRange(2400, 2600);
    }

    [Fact]
    public void FilterCandidates_ShouldRejectExcessiveOverlapInFavorOfHigherScore()
    {
        Point[] pts1 = [new Point(0, 0), new Point(200, 0), new Point(200, 200), new Point(0, 200)];
        var cand1 = new CropCandidate(pts1, new Rectangle(0, 0, 200, 200), 40000, new RotatedRect(new PointF(100, 100), new SizeF(200, 200), 0), 40000, 0.99, 0.99);

        // cand2 heavily overlaps cand1 (shifted by only 20px) with lower score
        Point[] pts2 = [new Point(20, 20), new Point(220, 20), new Point(220, 220), new Point(20, 220)];
        var cand2 = new CropCandidate(pts2, new Rectangle(20, 20, 200, 200), 40000, new RotatedRect(new PointF(120, 120), new SizeF(200, 200), 0), 30000, 0.80, 0.80);

        var filtered = CandidateResolutionFilter.FilterCandidates([cand1, cand2]);

        filtered.ShouldHaveSingleItem();
        filtered[0].Score.ShouldBe(40000);
    }
}
