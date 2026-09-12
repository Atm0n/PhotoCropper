using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Detection;
using System.Drawing;

namespace PhotoCropper.Tests.Detection;

public sealed class ForegroundMaskGeneratorTests
{
    [Fact]
    public void GeneratePrecomputedEdgeMap_ShouldProduceBinaryEdgeMap()
    {
        using Mat source = new(300, 300, DepthType.Cv8U, 3);
        source.SetTo(new MCvScalar(255, 255, 255));
        CvInvoke.Rectangle(source, new Rectangle(50, 50, 100, 100), new MCvScalar(0, 0, 0), -1);

        using Mat edges = ForegroundMaskGenerator.GeneratePrecomputedEdgeMap(source, 20, 50);

        Assert.False(edges.IsEmpty);
        Assert.Equal(300, edges.Width);
        Assert.Equal(300, edges.Height);
        Assert.Equal(1, edges.NumberOfChannels);
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

        Assert.False(foreground.IsEmpty);
        Assert.Equal(300, foreground.Width);
        Assert.Equal(300, foreground.Height);
    }
}
