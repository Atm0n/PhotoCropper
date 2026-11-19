using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OpenCvSharp;

namespace PhotoCropper.Gui.Services;

internal static class MatBitmapConverter
{
    public static Bitmap ToAvaloniaBitmap(Mat mat)
    {
        ArgumentNullException.ThrowIfNull(mat);

        if (mat.Empty() || mat.Width <= 0 || mat.Height <= 0)
        {
            throw new ArgumentException("Cannot convert an empty or zero-sized Mat to Bitmap.", nameof(mat));
        }

        if (mat.Channels() == 4)
        {
            return new Bitmap(
                PixelFormat.Bgra8888,
                AlphaFormat.Premul,
                mat.Data,
                new Avalonia.PixelSize(mat.Width, mat.Height),
                new Avalonia.Vector(96, 96),
                (int)mat.Step());
        }

        using Mat bgraMat = new();
        if (mat.Channels() == 1)
        {
            Cv2.CvtColor(mat, bgraMat, ColorConversionCodes.GRAY2BGRA);
        }
        else
        {
            Cv2.CvtColor(mat, bgraMat, ColorConversionCodes.BGR2BGRA);
        }

        return new Bitmap(
            PixelFormat.Bgra8888,
            AlphaFormat.Premul,
            bgraMat.Data,
            new Avalonia.PixelSize(bgraMat.Width, bgraMat.Height),
            new Avalonia.Vector(96, 96),
            (int)bgraMat.Step());
    }
}
