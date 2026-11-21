using OpenCvSharp;
using PhotoCropper.Core.Common;

namespace PhotoCropper.TestHelpers;

/// <summary>
/// A repeatable sequence of numbers for test images (SplitMix64), so the same seed always draws the same image on
/// every machine. Not for anything secret: it only has to look random and repeat exactly.
/// </summary>
public sealed class SeededRandom(int seed)
{
    private ulong _state = (ulong)(uint)seed;

    private ulong NextBits()
    {
        ulong z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>A number from 0 up to, not including, 1.</summary>
    public double NextDouble() => (NextBits() >> 11) * (1.0 / (1UL << 53));

    /// <summary>A whole number from <paramref name="min"/> up to, not including, <paramref name="max"/>.</summary>
    public int Next(int min, int max) => min + (int)((ulong)(max - min) * (NextBits() >> 32) >> 32);

    /// <summary>A whole number from 0 up to, not including, <paramref name="max"/>.</summary>
    public int Next(int max) => Next(0, max);

    /// <summary>A non-negative whole number.</summary>
    public int Next() => (int)(NextBits() >> 33);

    /// <summary>
    /// Fills an 8-bit or 16-bit signed image with whole numbers from <paramref name="low"/> up to, not including,
    /// <paramref name="high"/> (the same range on every channel).
    /// </summary>
    public void FillUniform(Mat image, int low, int high)
    {
        ArgumentNullException.ThrowIfNull(image);
        int count = image.Rows * image.Cols * image.Channels();
        if (image.Depth() == MatType.CV_16S)
        {
            var values = new short[count];
            for (int i = 0; i < count; i++) values[i] = (short)Next(low, high);
            image.SetData(values);
        }
        else
        {
            var values = new byte[count];
            for (int i = 0; i < count; i++) values[i] = (byte)Next(low, high);
            image.SetData(values);
        }
    }
}
