using Emgu.CV;
using PhotoCropper.Extraction;
using PhotoCropper.Tests.Helpers;

namespace PhotoCropper.Tests.Extraction;

public sealed class PhotoRestorationServiceTests
{
    [Fact]
    public void RestoreColors_ShouldPreserveDimensionsAndChannels_Bgr()
    {
        using Mat fadedBgr = TestImageFactory.CreateFadedPhoto(200, 200, includeAlpha: false);
        using Mat restoredBgr = PhotoRestorationService.RestoreColors(fadedBgr);

        Assert.False(restoredBgr.IsEmpty);
        Assert.Equal(200, restoredBgr.Width);
        Assert.Equal(200, restoredBgr.Height);
        Assert.Equal(3, restoredBgr.NumberOfChannels);
    }

    [Fact]
    public void RestoreColors_ShouldPreserveDimensionsAndChannels_Bgra()
    {
        using Mat fadedBgra = TestImageFactory.CreateFadedPhoto(150, 150, includeAlpha: true);
        using Mat restoredBgra = PhotoRestorationService.RestoreColors(fadedBgra);

        Assert.False(restoredBgra.IsEmpty);
        Assert.Equal(150, restoredBgra.Width);
        Assert.Equal(150, restoredBgra.Height);
        Assert.Equal(4, restoredBgra.NumberOfChannels);
    }

    [Fact]
    public void InpaintDustAndScratches_ShouldRemoveDefects()
    {
        using Mat photo = TestImageFactory.CreateBlemishedPhoto(200, 200);
        using Mat inpainted = PhotoRestorationService.InpaintDustAndScratches(photo);

        Assert.False(inpainted.IsEmpty);
        Assert.Equal(200, inpainted.Width);
        Assert.Equal(200, inpainted.Height);
        Assert.Equal(3, inpainted.NumberOfChannels);
    }
}
