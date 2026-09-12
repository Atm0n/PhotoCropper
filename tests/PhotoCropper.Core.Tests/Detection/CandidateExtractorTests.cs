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
}
