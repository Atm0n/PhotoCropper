using Emgu.CV;
using PhotoCropper.Core.Extraction;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Core.Tests.Extraction;

public sealed class PhotoRestorationServiceTests
{
    [Fact]
    public void RestoreColors_ShouldPreserveDimensionsAndChannels_Bgr()
    {
        using Mat fadedBgr = TestImageFactory.CreateFadedPhoto(200, 200, includeAlpha: false);
        using Mat restoredBgr = PhotoRestorationService.RestoreColors(fadedBgr);

        restoredBgr.IsEmpty.ShouldBeFalse();
        restoredBgr.Width.ShouldBe(200);
        restoredBgr.Height.ShouldBe(200);
        restoredBgr.NumberOfChannels.ShouldBe(3);
    }

    [Fact]
    public void RestoreColors_ShouldPreserveDimensionsAndChannels_Bgra()
    {
        using Mat fadedBgra = TestImageFactory.CreateFadedPhoto(150, 150, includeAlpha: true);
        using Mat restoredBgra = PhotoRestorationService.RestoreColors(fadedBgra);

        restoredBgra.IsEmpty.ShouldBeFalse();
        restoredBgra.Width.ShouldBe(150);
        restoredBgra.Height.ShouldBe(150);
        restoredBgra.NumberOfChannels.ShouldBe(4);
    }

    [Fact]
    public void InpaintDustAndScratches_ShouldRemoveDefects()
    {
        using Mat photo = TestImageFactory.CreateBlemishedPhoto(200, 200);
        using Mat inpainted = PhotoRestorationService.InpaintDustAndScratches(photo);

        inpainted.IsEmpty.ShouldBeFalse();
        inpainted.Width.ShouldBe(200);
        inpainted.Height.ShouldBe(200);
        inpainted.NumberOfChannels.ShouldBe(3);
    }
}
