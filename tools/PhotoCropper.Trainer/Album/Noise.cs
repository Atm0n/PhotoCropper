using OpenCvSharp;

namespace PhotoCropper.Trainer.Album;

/// <summary>Seeded noise fills, so a page made from a seed is the same on every run (OpenCV's own noise is global).</summary>
internal static class Noise
{
    /// <summary>Fills <paramref name="mat"/> (8-bit or 32-bit float, any channels) with normal noise.</summary>
    public static void FillNormal(Mat mat, Rng rng, double mean, double stdDev)
    {
        ArgumentNullException.ThrowIfNull(mat);
        ArgumentNullException.ThrowIfNull(rng);
        int count = mat.Rows * mat.Cols * mat.Channels();
        if (mat.Depth() == MatType.CV_32F)
        {
            var values = new float[count];
            for (int i = 0; i < count; i++) values[i] = (float)rng.Normal(mean, stdDev);
            Copy(values, mat);
        }
        else if (mat.Depth() == MatType.CV_8U)
        {
            var values = new byte[count];
            for (int i = 0; i < count; i++) values[i] = (byte)Math.Clamp(Math.Round(rng.Normal(mean, stdDev)), 0, 255);
            Copy(values, mat);
        }
        else
        {
            throw new ArgumentException("Only 8-bit and float images are supported.", nameof(mat));
        }
    }

    /// <summary>Copies pixel values (row by row, channels interleaved) into <paramref name="mat"/>.</summary>
    public static void Copy(Array values, Mat mat)
    {
        ArgumentNullException.ThrowIfNull(mat);
        using Mat source = Mat.FromPixelData(mat.Rows, mat.Cols, mat.Type(), values);
        source.CopyTo(mat);
    }

    /// <summary>
    /// Smooth, blotchy variation (paper fibres, uneven ageing): coarse noise scaled up. A float image of
    /// <paramref name="size"/>, one channel, mean 0, about <paramref name="amplitude"/> either way.
    /// </summary>
    public static Mat Blotches(Size size, Rng rng, double amplitude, int cell)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var coarseSize = new Size(Math.Max(2, size.Width / cell), Math.Max(2, size.Height / cell));
        using Mat coarse = new(coarseSize, MatType.CV_32FC1);
        FillNormal(coarse, rng, 0, amplitude);
        var result = new Mat();
        Cv2.Resize(coarse, result, size, 0, 0, InterpolationFlags.Cubic);
        return result;
    }
}
