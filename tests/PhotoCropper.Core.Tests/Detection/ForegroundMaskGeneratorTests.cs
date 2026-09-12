using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Core.Detection;
using System.Drawing;

namespace PhotoCropper.Core.Tests.Detection;

public sealed class ForegroundMaskGeneratorTests
{
    [Fact]
    public void GeneratePrecomputedEdgeMap_ShouldProduceBinaryEdgeMap()
    {
        using Mat source = new(300, 300, DepthType.Cv8U, 3);
        source.SetTo(new MCvScalar(255, 255, 255));
        CvInvoke.Rectangle(source, new Rectangle(50, 50, 100, 100), new MCvScalar(0, 0, 0), -1);

        using Mat edges = ForegroundMaskGenerator.GeneratePrecomputedEdgeMap(source, 20, 50);

        edges.IsEmpty.ShouldBeFalse();
        edges.Width.ShouldBe(300);
        edges.Height.ShouldBe(300);
        edges.NumberOfChannels.ShouldBe(1);
    }

    [Fact]
    public void PopulateForegroundMask_ShouldGenerateCombinedForeground()
    {
        using Mat source = new(300, 300, DepthType.Cv8U, 3);
        source.SetTo(new MCvScalar(255, 255, 255));
        CvInvoke.Rectangle(source, new Rectangle(50, 50, 100, 100), new MCvScalar(0, 0, 0), -1);

        using Mat hsv = new();
        CvInvoke.CvtColor(source, hsv, ColorConversion.Bgr2Hsv);
        MCvScalar bgHsv = BackgroundAnalyzer.SampleBackgroundColor(hsv);

        using Mat edges = ForegroundMaskGenerator.GeneratePrecomputedEdgeMap(source, 20, 50);
        using Mat foreground = new();

        ForegroundMaskGenerator.PopulateForegroundMask(
            source,
            foreground,
            bgHsv,
            25,
            20,
            50,
            edges,
            hsv);

        foreground.IsEmpty.ShouldBeFalse();
        foreground.Width.ShouldBe(300);
        foreground.Height.ShouldBe(300);
    }
}
