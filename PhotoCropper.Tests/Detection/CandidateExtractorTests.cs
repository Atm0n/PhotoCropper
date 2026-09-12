using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Detection;
using System.Drawing;

namespace PhotoCropper.Tests.Detection;

public sealed class CandidateExtractorTests
{
    [Fact]
    public void ExtractCandidates_ShouldExtractQuadrilaterals()
    {
        using Mat mask = new(500, 500, DepthType.Cv8U, 1);
        mask.SetTo(new MCvScalar(0));
        CvInvoke.Rectangle(mask, new Rectangle(50, 50, 200, 200), new MCvScalar(255), -1);

        var candidates = CandidateExtractor.ExtractCandidates(mask, 0, 500, 500, 0.01, 0.90);

        Assert.Single(candidates);
        Assert.InRange(candidates[0].Area, 38000, 42000);
        Assert.True(candidates[0].Rectangularity >= 0.9);
        Assert.True(candidates[0].Convexity >= 0.95);
    }

    [Fact]
    public void ExtractCandidates_ShouldRejectThinLines()
    {
        using Mat mask = new(500, 500, DepthType.Cv8U, 1);
        mask.SetTo(new MCvScalar(0));
        // Thin strip (400x5)
        CvInvoke.Rectangle(mask, new Rectangle(50, 50, 400, 5), new MCvScalar(255), -1);

        var candidates = CandidateExtractor.ExtractCandidates(mask, 0, 500, 500, 0.001, 0.90);

        Assert.Empty(candidates);
    }
}
