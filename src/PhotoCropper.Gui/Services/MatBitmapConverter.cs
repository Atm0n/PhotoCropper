using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Emgu.CV;
using Emgu.CV.CvEnum;

namespace PhotoCropperGui.Services;

internal static class MatBitmapConverter
{
    public static Bitmap ToAvaloniaBitmap(Mat mat)
    {
        ArgumentNullException.ThrowIfNull(mat);

        if (mat.NumberOfChannels == 4)
        {
            return new Bitmap(
                PixelFormat.Bgra8888,
                AlphaFormat.Premul,
                mat.DataPointer,
                new Avalonia.PixelSize(mat.Width, mat.Height),
                new Avalonia.Vector(96, 96),
                mat.Step);
        }

        using Mat bgraMat = new();
        CvInvoke.CvtColor(mat, bgraMat, ColorConversion.Bgr2Bgra);

        return new Bitmap(
            PixelFormat.Bgra8888,
            AlphaFormat.Premul,
            bgraMat.DataPointer,
            new Avalonia.PixelSize(bgraMat.Width, bgraMat.Height),
            new Avalonia.Vector(96, 96),
            bgraMat.Step);
    }
}
