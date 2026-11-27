using OpenCvSharp;
using PhotoCropper.Core.Detection;

namespace PhotoCropper.Core.Tests.Detection;

public sealed class CandidateExtractorTests
{
    [Fact]
    public void ExtractCandidatesShouldExtractQuadrilaterals()
    {
        using Mat mask = new(500, 500, MatType.CV_8UC1);
        mask.SetTo(new Scalar(0));
        Cv2.Rectangle(mask, new Rect(50, 50, 200, 200), new Scalar(255), -1);

        var candidates = CandidateExtractor.ExtractCandidates(mask, 0, 500, 500, 0.01, 0.90);

        candidates.ShouldHaveSingleItem();
        candidates[0].Area.ShouldBeInRange(38000, 42000);
        candidates[0].Rectangularity.ShouldBeGreaterThanOrEqualTo(0.9);
        candidates[0].Convexity.ShouldBeGreaterThanOrEqualTo(0.95);
    }

    [Fact]
    public void ExtractCandidatesShouldRejectThinLines()
    {
        using Mat mask = new(500, 500, MatType.CV_8UC1);
        mask.SetTo(new Scalar(0));
        // Thin strip (400x5)
        Cv2.Rectangle(mask, new Rect(50, 50, 400, 5), new Scalar(255), -1);

        var candidates = CandidateExtractor.ExtractCandidates(mask, 0, 500, 500, 0.001, 0.90);

        candidates.ShouldBeEmpty();
    }

    [Fact]
    public void ExtractCandidatesShouldRejectIncompleteNonRectangularShapes()
    {
        using Mat mask = new(500, 500, MatType.CV_8UC1);
        mask.SetTo(new Scalar(0));

        // Draw an L-shaped fragment (incomplete photo/shadow): 200x200 bounding box, but only 30px thick
        Cv2.Rectangle(mask, new Rect(50, 50, 30, 200), new Scalar(255), -1);
        Cv2.Rectangle(mask, new Rect(50, 220, 200, 30), new Scalar(255), -1);

        var candidates = CandidateExtractor.ExtractCandidates(mask, 0, 500, 500, 0.01, 0.90);

        // L-shape has very low rectangularity (~0.25) and low convexity, should be rejected
        candidates.ShouldBeEmpty();
    }

    [Fact]
    public void ExtractCandidatesShouldRejectUniformLidShadowArtifacts()
    {
        using Mat mask = new(500, 500, MatType.CV_8UC1);
        mask.SetTo(new Scalar(0));
        Cv2.Rectangle(mask, new Rect(50, 50, 200, 200), new Scalar(255), -1);

        // Color image: background is white (250, 250, 250), candidate region is a faint uniform shadow (242, 242, 242)
        using Mat colorImg = new(500, 500, MatType.CV_8UC3);
        colorImg.SetTo(new Scalar(250, 250, 250));
        Cv2.Rectangle(colorImg, new Rect(50, 50, 200, 200), new Scalar(242, 242, 242), -1);

        var candidates = CandidateExtractor.ExtractCandidates(
            mask,
            0,
            500,
            500,
            0.01,
            0.90,
            colorImg,
            new Scalar(250, 250, 250));

        // Uniform shadow without texture that is close to background color should be rejected
        candidates.ShouldBeEmpty();
    }
}
