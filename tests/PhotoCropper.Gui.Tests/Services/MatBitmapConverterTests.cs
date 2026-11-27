using OpenCvSharp;
using PhotoCropper.Gui.Services;

namespace PhotoCropper.Gui.Tests.Services;

public sealed class MatBitmapConverterTests
{
    public MatBitmapConverterTests()
    {
        TestAppBuilder.EnsureInitialized();
    }

    [Fact]
    public void ToAvaloniaBitmapNullMatShouldThrowArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => MatBitmapConverter.ToAvaloniaBitmap(null!));
    }

    [Fact]
    public void ToAvaloniaBitmap3ChannelBgrMatShouldConvertToBitmap()
    {
        using var mat = new Mat(100, 200, MatType.CV_8UC3);
        mat.SetTo(new Scalar(128, 64, 32));

        var bitmap = MatBitmapConverter.ToAvaloniaBitmap(mat);

        bitmap.ShouldNotBeNull();
        bitmap.PixelSize.Width.ShouldBe(200);
        bitmap.PixelSize.Height.ShouldBe(100);
    }

    [Fact]
    public void ToAvaloniaBitmap4ChannelBgraMatShouldConvertToBitmap()
    {
        using var mat = new Mat(50, 80, MatType.CV_8UC4);
        mat.SetTo(new Scalar(128, 64, 32, 255));

        var bitmap = MatBitmapConverter.ToAvaloniaBitmap(mat);

        bitmap.ShouldNotBeNull();
        bitmap.PixelSize.Width.ShouldBe(80);
        bitmap.PixelSize.Height.ShouldBe(50);
    }

    [Fact]
    public void ToAvaloniaBitmap1ChannelGrayMatShouldConvertToBitmap()
    {
        using var mat = new Mat(50, 80, MatType.CV_8UC1);
        mat.SetTo(new Scalar(128));

        var bitmap = MatBitmapConverter.ToAvaloniaBitmap(mat);

        bitmap.ShouldNotBeNull();
        bitmap.PixelSize.Width.ShouldBe(80);
        bitmap.PixelSize.Height.ShouldBe(50);
    }
}
