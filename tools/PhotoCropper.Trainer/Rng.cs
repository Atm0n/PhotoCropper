namespace PhotoCropper.Trainer;

/// <summary>
/// A small, fast, seedable random generator (xoshiro256**). The same seed gives the same album page on every machine
/// and .NET version, so a training set can be regenerated exactly. Not for anything security related.
/// </summary>
internal sealed class Rng
{
    private ulong _s0, _s1, _s2, _s3;
    private double? _spareNormal;

    public Rng(ulong seed)
    {
        // SplitMix64 spreads the seed over the four words of state.
        _s0 = SplitMix(ref seed);
        _s1 = SplitMix(ref seed);
        _s2 = SplitMix(ref seed);
        _s3 = SplitMix(ref seed);
    }

    private static ulong SplitMix(ref ulong x)
    {
        ulong z = x += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public ulong NextULong()
    {
        ulong result = ulong.RotateLeft(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = ulong.RotateLeft(_s3, 45);
        return result;
    }

    /// <summary>A number in [0, 1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    /// <summary>A number in [min, max).</summary>
    public double Range(double min, double max) => min + ((max - min) * NextDouble());

    /// <summary>An integer in [min, max).</summary>
    public int Range(int min, int max) => min + (int)(NextDouble() * (max - min));

    /// <summary>True with probability <paramref name="p"/>.</summary>
    public bool Chance(double p) => NextDouble() < p;

    /// <summary>A normally distributed number (Box–Muller).</summary>
    public double Normal(double mean = 0, double stdDev = 1)
    {
        if (_spareNormal is { } spare)
        {
            _spareNormal = null;
            return mean + (stdDev * spare);
        }
        double u = 1.0 - NextDouble(), v = NextDouble();
        double r = Math.Sqrt(-2.0 * Math.Log(u));
        _spareNormal = r * Math.Sin(2 * Math.PI * v);
        return mean + (stdDev * r * Math.Cos(2 * Math.PI * v));
    }

    /// <summary>One of <paramref name="items"/>, each as likely as its weight.</summary>
    public T Pick<T>(IReadOnlyList<(T Item, double Weight)> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        double total = items.Sum(i => i.Weight);
        double r = NextDouble() * total;
        foreach (var (item, weight) in items)
        {
            if (r < weight) return item;
            r -= weight;
        }
        return items[^1].Item;
    }

    /// <summary>One of <paramref name="items"/>, all equally likely.</summary>
    public T Pick<T>(IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items[Range(0, items.Count)];
    }

    /// <summary>A generator for a sub-task (a page's paper, one photo), independent of how much this one is used later.</summary>
    public Rng Fork() => new(NextULong());
}
