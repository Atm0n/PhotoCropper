using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Gui.Services;

namespace PhotoCropper.Gui.Tests.Gui;

public sealed class MatBitmapConverterTests
{
    public MatBitmapConverterTests()
    {
        TestAppBuilder.EnsureInitialized();
    }

    [Fact]
    public void ToAvaloniaBitmap_NullMat_ShouldThrowArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => MatBitmapConverter.ToAvaloniaBitmap(null!));
    }

    [Fact]
    public void ToAvaloniaBitmap_3ChannelBgrMat_ShouldConvertToBitmap()
    {
        using var mat = new Mat(100, 200, DepthType.Cv8U, 3);
        mat.SetTo(new MCvScalar(128, 64, 32));

        var bitmap = MatBitmapConverter.ToAvaloniaBitmap(mat);

        bitmap.ShouldNotBeNull();
        bitmap.PixelSize.Width.ShouldBe(200);
        bitmap.PixelSize.Height.ShouldBe(100);
    }

    [Fact]
    public void ToAvaloniaBitmap_4ChannelBgraMat_ShouldConvertToBitmap()
    {
        using var mat = new Mat(50, 80, DepthType.Cv8U, 4);
        mat.SetTo(new MCvScalar(128, 64, 32, 255));

        var bitmap = MatBitmapConverter.ToAvaloniaBitmap(mat);

        bitmap.ShouldNotBeNull();
        bitmap.PixelSize.Width.ShouldBe(80);
        bitmap.PixelSize.Height.ShouldBe(50);
    }
}
