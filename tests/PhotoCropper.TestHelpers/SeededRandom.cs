using OpenCvSharp;

namespace PhotoCropper.TestHelpers;

/// <summary>
/// A repeatable sequence of numbers for test images: OpenCV's own generator, started from a fixed seed, so the same
/// seed always draws the same image. It is a class so the generator's state moves on as it is passed around
/// (OpenCV's <see cref="RNG"/> is a struct and would restart from a copy).
/// </summary>
public sealed class SeededRandom(int seed)
{
    private RNG _rng = new((ulong)(uint)seed + 1); // OpenCV treats a zero state as a special case

    /// <summary>A whole number from <paramref name="min"/> up to, not including, <paramref name="max"/>.</summary>
    public int Next(int min, int max) => _rng.Uniform(min, max);

    /// <summary>A whole number from 0 up to, not including, <paramref name="max"/>.</summary>
    public int Next(int max) => _rng.Uniform(0, max);

    /// <summary>A non-negative whole number.</summary>
    public int Next() => _rng.Uniform(0, int.MaxValue);

    /// <summary>A number from 0 up to, not including, 1.</summary>
    public double NextDouble() => _rng.Uniform(0.0, 1.0);

    /// <summary>Fills <paramref name="image"/> evenly from <paramref name="low"/> up to, not including, <paramref name="high"/>.</summary>
    public void FillUniform(Mat image, Scalar low, Scalar high) => _rng.Fill(image, DistributionType.Uniform, low, high);
}
