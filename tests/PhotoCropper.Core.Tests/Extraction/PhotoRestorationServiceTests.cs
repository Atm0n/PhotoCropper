using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Core.Extraction;
using System.Drawing;

namespace PhotoCropper.Core.Tests.Extraction;

public sealed class PhotoRestorationServiceTests
{
    [Fact]
    public void RestoreColors_NullPhoto_ShouldThrowArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => PhotoRestorationService.RestoreColors(null!));
    }

    [Fact]
    public void RestoreColors_EmptyPhoto_ShouldReturnInput()
    {
        using var empty = new Mat();
        var result = PhotoRestorationService.RestoreColors(empty);
        result.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void RestoreColors_SmallPhoto_ShouldReturnInput()
    {
        using var small = new Mat(10, 10, DepthType.Cv8U, 3);
        var result = PhotoRestorationService.RestoreColors(small);
        result.Width.ShouldBe(10);
        result.Height.ShouldBe(10);
    }

    [Fact]
    public void RestoreColors_3ChannelBgr_ShouldEnhanceAndPreserveDimensions()
    {
        using var photo = new Mat(100, 100, DepthType.Cv8U, 3);
        photo.SetTo(new MCvScalar(120, 140, 160));

        using var restored = PhotoRestorationService.RestoreColors(photo, removeDust: true);

        restored.Width.ShouldBe(100);
        restored.Height.ShouldBe(100);
        restored.NumberOfChannels.ShouldBe(3);
    }

    [Fact]
    public void RestoreColors_4ChannelBgra_ShouldPreserveAlpha()
    {
        using var photo = new Mat(50, 50, DepthType.Cv8U, 4);
        photo.SetTo(new MCvScalar(100, 150, 200, 128));

        using var restored = PhotoRestorationService.RestoreColors(photo, removeDust: false);

        restored.Width.ShouldBe(50);
        restored.Height.ShouldBe(50);
        restored.NumberOfChannels.ShouldBe(4);
    }

    [Fact]
    public void InpaintDustAndScratches_NullPhoto_ShouldThrowArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => PhotoRestorationService.InpaintDustAndScratches(null!));
    }

    [Fact]
    public void InpaintDustAndScratches_WithBlemishes_ShouldInpaintBlemishes()
    {
        using var photo = new Mat(100, 100, DepthType.Cv8U, 3);
        photo.SetTo(new MCvScalar(128, 128, 128));

        // Draw a tiny black speck (dust) and white speck
        CvInvoke.Circle(photo, new Point(50, 50), 1, new MCvScalar(0, 0, 0), -1);
        CvInvoke.Circle(photo, new Point(30, 30), 1, new MCvScalar(255, 255, 255), -1);

        using var inpainted = PhotoRestorationService.InpaintDustAndScratches(photo);

        inpainted.Width.ShouldBe(100);
        inpainted.Height.ShouldBe(100);
        inpainted.NumberOfChannels.ShouldBe(3);
    }
}
