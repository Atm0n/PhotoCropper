using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Extraction;
using PhotoCropper.Tests.Helpers;

namespace PhotoCropper.Tests.Extraction;

public sealed class OrientationTests
{
    [Fact]
    public void AutoOrientationService_ShouldDetectSkyOrientationCorrectly()
    {
        // 1. Upright photo: Sky at top, Ground at bottom
        using Mat upright = TestImageFactory.CreateSkyLandscapePhoto(200, 200, 0);
        Assert.Equal(0, AutoOrientationService.DetectRequiredRotation(upright));

        // 2. Upside-down photo: Sky at bottom, Ground at top
        using Mat upsideDown = TestImageFactory.CreateSkyLandscapePhoto(200, 200, 180);
        Assert.Equal(180, AutoOrientationService.DetectRequiredRotation(upsideDown));

        // Test OrientPhoto rotates 180
        using Mat oriented = AutoOrientationService.OrientPhoto(upsideDown);
        Assert.Equal(0, AutoOrientationService.DetectRequiredRotation(oriented));

        // 3. Sideways photo: Sky on left -> needs 90 CW rotation
        using Mat sidewaysLeft = TestImageFactory.CreateSkyLandscapePhoto(200, 200, 90);
        Assert.Equal(90, AutoOrientationService.DetectRequiredRotation(sidewaysLeft));
    }

    [Fact]
    public void FaceOrientationService_ModelShouldBeAvailableAndEmbedded()
    {
        Assert.True(FaceOrientationService.IsModelAvailable);
    }

    [Fact]
    public void FaceOrientationService_NonFaceImage_ShouldReturnNegativeOne()
    {
        using Mat landscape = new(200, 200, DepthType.Cv8U, 3);
        landscape.SetTo(new MCvScalar(200, 100, 50));
        Assert.Equal(-1, FaceOrientationService.DetectFaceRotation(landscape));
    }

    [Fact]
    public void AutoOrientationService_ShouldHandleBgra4ChannelAndGrayscale()
    {
        // 4-channel BGRA (transparent margins from extracted photos)
        using Mat bgra = new(200, 200, DepthType.Cv8U, 4);
        bgra.SetTo(new MCvScalar(200, 100, 50, 255));
        Assert.Equal(0, AutoOrientationService.DetectRequiredRotation(bgra));

        // 1-channel Grayscale
        using Mat gray = new(200, 200, DepthType.Cv8U, 1);
        gray.SetTo(new MCvScalar(128));
        Assert.Equal(0, AutoOrientationService.DetectRequiredRotation(gray));
    }
}
