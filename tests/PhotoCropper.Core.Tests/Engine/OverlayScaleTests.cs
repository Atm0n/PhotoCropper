using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Core.Tests.Engine;

public sealed class OverlayScaleTests : IDisposable
{
    private readonly string _tempDir = TestImageFactory.CreateTempDirectory("OverlayScaleTests");

    private PhotoCropperEngine CreateEngine(int width, int height)
    {
        string path = Path.Combine(_tempDir, $"scan_{width}x{height}.png");
        using (var scan = new Mat(height, width, DepthType.Cv8U, 3))
        {
            scan.SetTo(new MCvScalar(240, 240, 240));
            scan.Save(path);
        }
        return new PhotoCropperEngine(path);
    }

    [Fact]
    public void OverlayScale_AtReferenceSize_ShouldBeOne()
    {
        using var engine = CreateEngine(3500, 2480);

        engine.OverlayScale.ShouldBe(1.0, 0.001);
        engine.ScaleOverlay(45).ShouldBe(45);
    }

    [Fact]
    public void OverlayScale_ShouldGrowWithResolutionSoHandlesKeepTheirRelativeSize()
    {
        using var lowRes = CreateEngine(1750, 1240);   // ~150 DPI A4
        using var highRes = CreateEngine(7000, 4960);  // ~600 DPI A4

        lowRes.ScaleOverlay(60).ShouldBe(30);
        highRes.ScaleOverlay(60).ShouldBe(120);
    }

    [Fact]
    public void ScaleOverlay_ShouldNeverReturnLessThanOnePixel()
    {
        using var tiny = CreateEngine(200, 200);

        tiny.ScaleOverlay(1).ShouldBeGreaterThanOrEqualTo(1);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }
        catch (IOException)
        {
        }
    }
}
