using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Core.Detection;
using System.Drawing;

namespace PhotoCropper.Core.Tests.Detection;

public sealed class CandidateExtractorTests
{
    [Fact]
    public void ExtractCandidates_ShouldExtractQuadrilaterals()
    {
        using Mat mask = new(500, 500, DepthType.Cv8U, 1);
        mask.SetTo(new MCvScalar(0));
        CvInvoke.Rectangle(mask, new Rectangle(50, 50, 200, 200), new MCvScalar(255), -1);

        var candidates = CandidateExtractor.ExtractCandidates(mask, 0, 500, 500, 0.01, 0.90);

        candidates.ShouldHaveSingleItem();
        candidates[0].Area.ShouldBeInRange(38000, 42000);
        candidates[0].Rectangularity.ShouldBeGreaterThanOrEqualTo(0.9);
        candidates[0].Convexity.ShouldBeGreaterThanOrEqualTo(0.95);
    }

    [Fact]
    public void ExtractCandidates_ShouldRejectThinLines()
    {
        using Mat mask = new(500, 500, DepthType.Cv8U, 1);
        mask.SetTo(new MCvScalar(0));
        // Thin strip (400x5)
        CvInvoke.Rectangle(mask, new Rectangle(50, 50, 400, 5), new MCvScalar(255), -1);

        var candidates = CandidateExtractor.ExtractCandidates(mask, 0, 500, 500, 0.001, 0.90);

        candidates.ShouldBeEmpty();
    }

    [Fact]
    public void ExtractCandidates_ShouldRejectIncompleteNonRectangularShapes()
    {
        using Mat mask = new(500, 500, DepthType.Cv8U, 1);
        mask.SetTo(new MCvScalar(0));

        // Draw an L-shaped fragment (incomplete photo/shadow): 200x200 bounding box, but only 30px thick
        CvInvoke.Rectangle(mask, new Rectangle(50, 50, 30, 200), new MCvScalar(255), -1);
        CvInvoke.Rectangle(mask, new Rectangle(50, 220, 200, 30), new MCvScalar(255), -1);

        var candidates = CandidateExtractor.ExtractCandidates(mask, 0, 500, 500, 0.01, 0.90);

        // L-shape has very low rectangularity (~0.25) and low convexity, should be rejected
        candidates.ShouldBeEmpty();
    }

    [Fact]
    public void ExtractCandidates_ShouldRejectUniformLidShadowArtifacts()
    {
        using Mat mask = new(500, 500, DepthType.Cv8U, 1);
        mask.SetTo(new MCvScalar(0));
        CvInvoke.Rectangle(mask, new Rectangle(50, 50, 200, 200), new MCvScalar(255), -1);

        // Color image: background is white (250, 250, 250), candidate region is a faint uniform shadow (242, 242, 242)
        using Mat colorImg = new(500, 500, DepthType.Cv8U, 3);
        colorImg.SetTo(new MCvScalar(250, 250, 250));
        CvInvoke.Rectangle(colorImg, new Rectangle(50, 50, 200, 200), new MCvScalar(242, 242, 242), -1);

        var candidates = CandidateExtractor.ExtractCandidates(
            mask,
            0,
            500,
            500,
            0.01,
            0.90,
            colorImg,
            new MCvScalar(250, 250, 250));

        // Uniform shadow without texture that is close to background color should be rejected
        candidates.ShouldBeEmpty();
    }
}
