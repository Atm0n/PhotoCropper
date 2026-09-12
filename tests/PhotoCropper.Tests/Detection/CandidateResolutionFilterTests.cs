using Emgu.CV.Structure;
using PhotoCropper.Detection;
using PhotoCropper.Models;
using System.Drawing;

namespace PhotoCropper.Tests.Detection;

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
        Assert.Equal(2, filtered.Count);
        Assert.DoesNotContain(filtered, c => c.Area == 500000);
    }

    [Fact]
    public void FilterCandidates_ShouldPreserveSeparatedCandidates()
    {
        Point[] pts1 = [new Point(0, 0), new Point(400, 0), new Point(400, 400), new Point(0, 400)];
        var cand1 = new CropCandidate(pts1, new Rectangle(0, 0, 400, 400), 160000, new RotatedRect(new PointF(200, 200), new SizeF(400, 400), 0), 160000, 0.98, 0.99);

        Point[] pts2 = [new Point(500, 0), new Point(900, 0), new Point(900, 400), new Point(500, 400)];
        var cand2 = new CropCandidate(pts2, new Rectangle(500, 0, 400, 400), 160000, new RotatedRect(new PointF(700, 200), new SizeF(400, 400), 0), 160000, 0.98, 0.99);

        var filtered = CandidateResolutionFilter.FilterCandidates([cand1, cand2]);
        Assert.Equal(2, filtered.Count);
    }
}
