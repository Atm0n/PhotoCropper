using OpenCvSharp;
using PhotoCropper.Core.Detection;

namespace PhotoCropper.Core.Tests.Detection;

public sealed class ForegroundMaskGeneratorTests
{
    [Fact]
    public void GeneratePrecomputedEdgeMap_ShouldProduceBinaryEdgeMap()
    {
        using Mat source = new(300, 300, MatType.CV_8UC3);
        source.SetTo(new Scalar(255, 255, 255));
        Cv2.Rectangle(source, new Rect(50, 50, 100, 100), new Scalar(0, 0, 0), -1);

        using Mat edges = ForegroundMaskGenerator.GeneratePrecomputedEdgeMap(source, 20, 50);

        edges.Empty().ShouldBeFalse();
        edges.Width.ShouldBe(300);
        edges.Height.ShouldBe(300);
        edges.Channels().ShouldBe(1);
    }

    [Fact]
    public void PopulateForegroundMask_ShouldGenerateCombinedForeground()
    {
        using Mat source = new(300, 300, MatType.CV_8UC3);
        source.SetTo(new Scalar(255, 255, 255));
        Cv2.Rectangle(source, new Rect(50, 50, 100, 100), new Scalar(0, 0, 0), -1);

        using Mat hsv = new();
        Cv2.CvtColor(source, hsv, ColorConversionCodes.BGR2HSV);
        Scalar bgHsv = BackgroundAnalyzer.SampleBackgroundColor(hsv);

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

        foreground.Empty().ShouldBeFalse();
        foreground.Width.ShouldBe(300);
        foreground.Height.ShouldBe(300);
    }

    [Fact]
    public void PopulateOtsuForegroundMask_ShouldGenerateOtsuMask()
    {
        using Mat source = new(300, 300, MatType.CV_8UC3);
        source.SetTo(new Scalar(255, 255, 255));
        Cv2.Rectangle(source, new Rect(50, 50, 100, 100), new Scalar(20, 20, 20), -1);

        using Mat edges = ForegroundMaskGenerator.GeneratePrecomputedEdgeMap(source, 20, 50);
        using Mat foreground = new();

        ForegroundMaskGenerator.PopulateOtsuForegroundMask(
            source,
            foreground,
            20,
            50,
            isLightBackground: true,
            edges);

        foreground.Empty().ShouldBeFalse();
        foreground.Width.ShouldBe(300);
        foreground.Height.ShouldBe(300);
        Cv2.CountNonZero(foreground).ShouldBeGreaterThan(5000);
    }
}
