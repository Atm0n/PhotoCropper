using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Core.Extraction;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Core.Tests.Extraction;

public sealed class OrientationTests
{
    [Fact]
    public void AutoOrientationService_ShouldDetectSkyOrientationCorrectly()
    {
        // 1. Upright photo: Sky at top, Ground at bottom
        using Mat upright = TestImageFactory.CreateSkyLandscapePhoto(200, 200, 0);
        AutoOrientationService.DetectRequiredRotation(upright).ShouldBe(0);

        // 2. Upside-down photo: Sky at bottom, Ground at top
        using Mat upsideDown = TestImageFactory.CreateSkyLandscapePhoto(200, 200, 180);
        AutoOrientationService.DetectRequiredRotation(upsideDown).ShouldBe(180);

        // Test OrientPhoto rotates 180
        using Mat oriented = AutoOrientationService.OrientPhoto(upsideDown);
        AutoOrientationService.DetectRequiredRotation(oriented).ShouldBe(0);

        // 3. Sideways photo: Sky on left -> needs 90 CW rotation
        using Mat sidewaysLeft = TestImageFactory.CreateSkyLandscapePhoto(200, 200, 90);
        AutoOrientationService.DetectRequiredRotation(sidewaysLeft).ShouldBe(90);
    }

    [Fact]
    public void FaceOrientationService_ModelShouldBeAvailableAndEmbedded()
    {
        FaceOrientationService.IsModelAvailable.ShouldBeTrue();
    }

    [Fact]
    public void FaceOrientationService_NonFaceImage_ShouldReturnNegativeOne()
    {
        using Mat landscape = new(200, 200, DepthType.Cv8U, 3);
        landscape.SetTo(new MCvScalar(200, 100, 50));
        FaceOrientationService.DetectFaceRotation(landscape).ShouldBe(-1);
    }

    [Fact]
    public void AutoOrientationService_ShouldHandleBgra4ChannelAndGrayscale()
    {
        // 4-channel BGRA (transparent margins from extracted photos)
        using Mat bgra = new(200, 200, DepthType.Cv8U, 4);
        bgra.SetTo(new MCvScalar(200, 100, 50, 255));
        AutoOrientationService.DetectRequiredRotation(bgra).ShouldBe(0);

        // 1-channel Grayscale
        using Mat gray = new(200, 200, DepthType.Cv8U, 1);
        gray.SetTo(new MCvScalar(128));
        AutoOrientationService.DetectRequiredRotation(gray).ShouldBe(0);
    }
}
