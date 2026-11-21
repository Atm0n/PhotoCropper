using OpenCvSharp;
using PhotoCropper.Core.Detection;

namespace PhotoCropper.Core.Tests.Detection;

public sealed class BackgroundAnalyzerTests
{
    [Fact]
    public void SampleBackgroundColor_WhiteBackground_ShouldReturnHighValue()
    {
        using Mat whiteBgr = new(500, 500, MatType.CV_8UC3);
        whiteBgr.SetTo(new Scalar(255, 255, 255));
        using Mat whiteHsv = new();
        Cv2.CvtColor(whiteBgr, whiteHsv, ColorConversionCodes.BGR2HSV);

        Scalar bgHsv = BackgroundAnalyzer.SampleBackgroundColor(whiteHsv);
        bgHsv.Val2.ShouldBeGreaterThanOrEqualTo(250);
    }

    [Fact]
    public void SampleBackgroundColor_DarkBackground_ShouldReturnLowValue()
    {
        using Mat darkBgr = new(500, 500, MatType.CV_8UC3);
        darkBgr.SetTo(new Scalar(20, 20, 20));
        using Mat darkHsv = new();
        Cv2.CvtColor(darkBgr, darkHsv, ColorConversionCodes.BGR2HSV);

        Scalar bgHsv = BackgroundAnalyzer.SampleBackgroundColor(darkHsv);
        bgHsv.Val2.ShouldBeLessThanOrEqualTo(25);
    }

    [Fact]
    public void FindBedAroundPage_PageOnLightBed_ShouldTakeTheBedAndThePagesEdge()
    {
        string dir = PhotoCropper.TestHelpers.TestImageFactory.CreateTempDirectory("BedAroundPage");
        try
        {
            string path = Path.Combine(dir, "page.png");
            PhotoCropper.TestHelpers.TestImageFactory.CreateAlbumPageOnBedScan(path, paperGrey: 94, edgeDarken: 14);
            using Mat scan = Cv2.ImRead(path);
            using Mat hsv = new();
            Cv2.CvtColor(scan, hsv, ColorConversionCodes.BGR2HSV);
            Scalar sampled = BackgroundAnalyzer.SampleBackgroundColor(hsv);

            using Mat? around = BackgroundAnalyzer.FindBedAroundPage(scan, sampled, out Scalar page);

            around.ShouldNotBeNull();
            around.At<byte>(10, 10).ShouldBe((byte)255);   // the bed
            around.At<byte>(70, 800).ShouldBe((byte)255);  // the page's darker edge
            around.At<byte>(1000, 880).ShouldBe((byte)0);  // the page between photos
            around.At<byte>(170, 400).ShouldBe((byte)0);   // a photo's light border, 100 px from the page's edge
            page.Val2.ShouldBeInRange(90, 98);               // the paper inside its darker edge
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FindBedAroundPage_PhotosOnTheBed_ShouldFindNothing()
    {
        using Mat scan = new(1000, 800, MatType.CV_8UC3, new Scalar(240, 240, 240));
        Cv2.Rectangle(scan, new Rect(100, 100, 300, 200), new Scalar(60, 90, 120), -1);
        using Mat hsv = new();
        Cv2.CvtColor(scan, hsv, ColorConversionCodes.BGR2HSV);

        using Mat? around = BackgroundAnalyzer.FindBedAroundPage(scan, BackgroundAnalyzer.SampleBackgroundColor(hsv), out _);

        around.ShouldBeNull();
    }

    [Fact]
    public void SamplePixelBackgroundColor_ShouldSampleLocalNeighborhood()
    {
        using Mat bgr = new(100, 100, MatType.CV_8UC3);
        bgr.SetTo(new Scalar(255, 255, 255));
        Cv2.Rectangle(bgr, new Rect(40, 40, 20, 20), new Scalar(0, 0, 0), -1);

        Scalar sample = BackgroundAnalyzer.SamplePixelBackgroundColor(bgr, 50, 50);
        sample.Val2.ShouldBeLessThanOrEqualTo(10);
    }

    [Fact]
    public void HsvToBgr_ShouldConvertAccurately()
    {
        // White in HSV (0, 0, 255)
        Scalar whiteHsv = new(0, 0, 255);
        Scalar bgr = BackgroundAnalyzer.HsvToBgr(whiteHsv);

        bgr.Val0.ShouldBeInRange(250, 256);
        bgr.Val1.ShouldBeInRange(250, 256);
        bgr.Val2.ShouldBeInRange(250, 256);
    }

    [Fact]
    public void CreateBackgroundMask_ShouldIsolateBackgroundPixels()
    {
        using Mat bgr = new(200, 200, MatType.CV_8UC3);
        bgr.SetTo(new Scalar(255, 255, 255)); // White background
        Cv2.Rectangle(bgr, new Rect(50, 50, 100, 100), new Scalar(0, 0, 0), -1); // Black photo

        using Mat hsv = new();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);

        Scalar avgColor = BackgroundAnalyzer.SampleBackgroundColor(hsv);
        using Mat mask = BackgroundAnalyzer.CreateBackgroundMask(hsv, avgColor, 30);

        mask.Empty().ShouldBeFalse();
        mask.Width.ShouldBe(200);
        mask.Height.ShouldBe(200);
    }

    [Fact]
    public void SampleBackgroundColor_WithScannerBezel_ShouldIgnoreBezelAndSampleBed()
    {
        using Mat bgr = new(1000, 1000, MatType.CV_8UC3);
        bgr.SetTo(new Scalar(255, 255, 255)); // White bed

        // 12px dark bezel on all edges (typical of flatbed scanners)
        Scalar bezel = new(10, 10, 10);
        Cv2.Rectangle(bgr, new Rect(0, 0, 1000, 12), bezel, -1);
        Cv2.Rectangle(bgr, new Rect(0, 988, 1000, 12), bezel, -1);
        Cv2.Rectangle(bgr, new Rect(0, 0, 12, 1000), bezel, -1);
        Cv2.Rectangle(bgr, new Rect(988, 0, 12, 1000), bezel, -1);

        using Mat hsv = new();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);

        Scalar bgHsv = BackgroundAnalyzer.SampleBackgroundColor(hsv);
        bgHsv.Val2.ShouldBeGreaterThanOrEqualTo(240); // Should accurately sample the white bed, not the bezel
    }

    [Fact]
    public void DetectBezelMargins_WithPerimeterBezel_ShouldAccuratelyDetectThickness()
    {
        using Mat bgr = new(1000, 1000, MatType.CV_8UC3);
        bgr.SetTo(new Scalar(255, 255, 255)); // White bed

        // Top 15px bezel, Left 12px bezel
        Scalar bezel = new(10, 10, 10);
        Cv2.Rectangle(bgr, new Rect(0, 0, 1000, 15), bezel, -1);
        Cv2.Rectangle(bgr, new Rect(0, 0, 12, 1000), bezel, -1);

        Scalar whiteHsv = new(0, 0, 255);
        var (Top, Bottom, Left, Right) = BackgroundAnalyzer.DetectBezelMargins(bgr, whiteHsv, 30);

        Top.ShouldBeInRange(10, 20);
        Left.ShouldBeInRange(10, 20);
        Bottom.ShouldBe(0);
        Right.ShouldBe(0);
    }

    [Fact]
    public void DetectBezelMargins_WithoutBezel_ShouldReturnZeroes()
    {
        using Mat bgr = new(1000, 1000, MatType.CV_8UC3);
        bgr.SetTo(new Scalar(255, 255, 255)); // White bed

        Scalar whiteHsv = new(0, 0, 255);
        var (Top, Bottom, Left, Right) = BackgroundAnalyzer.DetectBezelMargins(bgr, whiteHsv, 30);

        Top.ShouldBe(0);
        Bottom.ShouldBe(0);
        Left.ShouldBe(0);
        Right.ShouldBe(0);
    }
}
