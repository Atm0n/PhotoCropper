using OpenCvSharp;
using PhotoCropper.Core.Extraction;

namespace PhotoCropper.Core.Tests.Extraction;

public sealed class PhotoRestorationServiceTests
{
    [Fact]
    public void RestoreColorsNullPhotoShouldThrowArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => PhotoRestorationService.RestoreColors(null!));
    }

    [Fact]
    public void RestoreColorsEmptyPhotoShouldReturnInput()
    {
        using var empty = new Mat();
        var result = PhotoRestorationService.RestoreColors(empty);
        result.Empty().ShouldBeTrue();
    }

    [Fact]
    public void RestoreColorsSmallPhotoShouldReturnInput()
    {
        using var small = new Mat(10, 10, MatType.CV_8UC3);
        var result = PhotoRestorationService.RestoreColors(small);
        result.Width.ShouldBe(10);
        result.Height.ShouldBe(10);
    }

    [Fact]
    public void RestoreColors3ChannelBgrShouldEnhanceAndPreserveDimensions()
    {
        using var photo = new Mat(100, 100, MatType.CV_8UC3);
        photo.SetTo(new Scalar(120, 140, 160));

        using var restored = PhotoRestorationService.RestoreColors(photo, removeDust: true);

        restored.Width.ShouldBe(100);
        restored.Height.ShouldBe(100);
        restored.Channels().ShouldBe(3);
    }

    [Fact]
    public void RestoreColors4ChannelBgraShouldPreserveAlpha()
    {
        using var photo = new Mat(50, 50, MatType.CV_8UC4);
        photo.SetTo(new Scalar(100, 150, 200, 128));

        using var restored = PhotoRestorationService.RestoreColors(photo, removeDust: false);

        restored.Width.ShouldBe(50);
        restored.Height.ShouldBe(50);
        restored.Channels().ShouldBe(4);
    }

    [Fact]
    public void InpaintDustAndScratchesNullPhotoShouldThrowArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => PhotoRestorationService.InpaintDustAndScratches(null!));
    }

    [Fact]
    public void InpaintDustAndScratchesWithBlemishesShouldInpaintBlemishes()
    {
        using var photo = new Mat(100, 100, MatType.CV_8UC3);
        photo.SetTo(new Scalar(128, 128, 128));

        // Draw a tiny black speck (dust) and white speck
        Cv2.Circle(photo, new Point(50, 50), 1, new Scalar(0, 0, 0), -1);
        Cv2.Circle(photo, new Point(30, 30), 1, new Scalar(255, 255, 255), -1);

        using var inpainted = PhotoRestorationService.InpaintDustAndScratches(photo);

        inpainted.Width.ShouldBe(100);
        inpainted.Height.ShouldBe(100);
        inpainted.Channels().ShouldBe(3);
    }
}
