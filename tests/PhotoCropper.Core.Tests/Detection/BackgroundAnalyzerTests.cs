using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Core.Detection;
using System.Drawing;

namespace PhotoCropper.Core.Tests.Detection;

public sealed class BackgroundAnalyzerTests
{
    [Fact]
    public void SampleBackgroundColor_WhiteBackground_ShouldReturnHighValue()
    {
        using Mat whiteBgr = new(500, 500, DepthType.Cv8U, 3);
        whiteBgr.SetTo(new MCvScalar(255, 255, 255));
        using Mat whiteHsv = new();
        CvInvoke.CvtColor(whiteBgr, whiteHsv, ColorConversion.Bgr2Hsv);

        MCvScalar bgHsv = BackgroundAnalyzer.SampleBackgroundColor(whiteHsv);
        bgHsv.V2.ShouldBeGreaterThanOrEqualTo(250);
    }

    [Fact]
    public void SampleBackgroundColor_DarkBackground_ShouldReturnLowValue()
    {
        using Mat darkBgr = new(500, 500, DepthType.Cv8U, 3);
        darkBgr.SetTo(new MCvScalar(20, 20, 20));
        using Mat darkHsv = new();
        CvInvoke.CvtColor(darkBgr, darkHsv, ColorConversion.Bgr2Hsv);

        MCvScalar bgHsv = BackgroundAnalyzer.SampleBackgroundColor(darkHsv);
        bgHsv.V2.ShouldBeLessThanOrEqualTo(25);
    }

    [Fact]
    public void SamplePixelBackgroundColor_ShouldSampleLocalNeighborhood()
    {
        using Mat bgr = new(100, 100, DepthType.Cv8U, 3);
        bgr.SetTo(new MCvScalar(255, 255, 255));
        CvInvoke.Rectangle(bgr, new Rectangle(40, 40, 20, 20), new MCvScalar(0, 0, 0), -1);

        MCvScalar sample = BackgroundAnalyzer.SamplePixelBackgroundColor(bgr, 50, 50);
        sample.V2.ShouldBeLessThanOrEqualTo(10);
    }

    [Fact]
    public void HsvToBgr_ShouldConvertAccurately()
    {
        // White in HSV (0, 0, 255)
        MCvScalar whiteHsv = new(0, 0, 255);
        MCvScalar bgr = BackgroundAnalyzer.HsvToBgr(whiteHsv);

        bgr.V0.ShouldBeInRange(250, 256);
        bgr.V1.ShouldBeInRange(250, 256);
        bgr.V2.ShouldBeInRange(250, 256);
    }

    [Fact]
    public void CreateBackgroundMask_ShouldIsolateBackgroundPixels()
    {
        using Mat bgr = new(200, 200, DepthType.Cv8U, 3);
        bgr.SetTo(new MCvScalar(255, 255, 255)); // White background
        CvInvoke.Rectangle(bgr, new Rectangle(50, 50, 100, 100), new MCvScalar(0, 0, 0), -1); // Black photo

        using Mat hsv = new();
        CvInvoke.CvtColor(bgr, hsv, ColorConversion.Bgr2Hsv);

        MCvScalar avgColor = BackgroundAnalyzer.SampleBackgroundColor(hsv);
        using Mat mask = BackgroundAnalyzer.CreateBackgroundMask(hsv, avgColor, 30);

        mask.IsEmpty.ShouldBeFalse();
        mask.Width.ShouldBe(200);
        mask.Height.ShouldBe(200);
    }

    [Fact]
    public void SampleBackgroundColor_WithScannerBezel_ShouldIgnoreBezelAndSampleBed()
    {
        using Mat bgr = new(1000, 1000, DepthType.Cv8U, 3);
        bgr.SetTo(new MCvScalar(255, 255, 255)); // White bed

        // 12px dark bezel on all edges (typical of flatbed scanners)
        MCvScalar bezel = new(10, 10, 10);
        CvInvoke.Rectangle(bgr, new Rectangle(0, 0, 1000, 12), bezel, -1);
        CvInvoke.Rectangle(bgr, new Rectangle(0, 988, 1000, 12), bezel, -1);
        CvInvoke.Rectangle(bgr, new Rectangle(0, 0, 12, 1000), bezel, -1);
        CvInvoke.Rectangle(bgr, new Rectangle(988, 0, 12, 1000), bezel, -1);

        using Mat hsv = new();
        CvInvoke.CvtColor(bgr, hsv, ColorConversion.Bgr2Hsv);

        MCvScalar bgHsv = BackgroundAnalyzer.SampleBackgroundColor(hsv);
        bgHsv.V2.ShouldBeGreaterThanOrEqualTo(240); // Should accurately sample the white bed, not the bezel
    }

    [Fact]
    public void DetectBezelMargins_WithPerimeterBezel_ShouldAccuratelyDetectThickness()
    {
        using Mat bgr = new(1000, 1000, DepthType.Cv8U, 3);
        bgr.SetTo(new MCvScalar(255, 255, 255)); // White bed

        // Top 15px bezel, Left 12px bezel
        MCvScalar bezel = new(10, 10, 10);
        CvInvoke.Rectangle(bgr, new Rectangle(0, 0, 1000, 15), bezel, -1);
        CvInvoke.Rectangle(bgr, new Rectangle(0, 0, 12, 1000), bezel, -1);

        MCvScalar whiteHsv = new(0, 0, 255);
        var margins = BackgroundAnalyzer.DetectBezelMargins(bgr, whiteHsv, 30);

        margins.Top.ShouldBeInRange(10, 20);
        margins.Left.ShouldBeInRange(10, 20);
        margins.Bottom.ShouldBe(0);
        margins.Right.ShouldBe(0);
    }

    [Fact]
    public void DetectBezelMargins_WithoutBezel_ShouldReturnZeroes()
    {
        using Mat bgr = new(1000, 1000, DepthType.Cv8U, 3);
        bgr.SetTo(new MCvScalar(255, 255, 255)); // White bed

        MCvScalar whiteHsv = new(0, 0, 255);
        var margins = BackgroundAnalyzer.DetectBezelMargins(bgr, whiteHsv, 30);

        margins.Top.ShouldBe(0);
        margins.Bottom.ShouldBe(0);
        margins.Left.ShouldBe(0);
        margins.Right.ShouldBe(0);
    }
}
