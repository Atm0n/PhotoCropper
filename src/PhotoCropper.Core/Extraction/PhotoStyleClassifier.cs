using OpenCvSharp;
using PhotoCropper.Core.Common;

namespace PhotoCropper.Core.Extraction;

/// <summary>What kind of print a photo is, which decides how it is best restored.</summary>
public enum PhotoStyle
{
    /// <summary>Not looked at yet.</summary>
    Unknown,

    /// <summary>Black and white, possibly yellowed a little with age.</summary>
    BlackAndWhite,

    /// <summary>Black and white with a clear warm tone (sepia, or strongly yellowed).</summary>
    Sepia,

    /// <summary>Colour, with one colour cast dominating (faded prints going red, magenta or yellow).</summary>
    FadedColor,

    /// <summary>Colour without a dominating cast: usually fine as scanned.</summary>
    Color
}

/// <summary>
/// Sorts photos into <see cref="PhotoStyle"/>s from their colours, so photos of a kind can be restored together.
/// </summary>
/// <remarks>
/// Measured in Lab on a small copy. A black-and-white print's colours, even an aged or toned one, vary along a single
/// tint (lighter or darker, warmer or cooler), with no blues or greens; a colour photo's spread in every direction.
/// A faded colour print's colour is mostly one cast. Thresholds were set on 126 real album photos (1950s–2000s).
/// </remarks>
public static class PhotoStyleClassifier
{
    /// <summary>Spread (standard deviation) across the main tint direction below which a photo is black and white.</summary>
    internal const double MaxMonochromeCrossSpread = 2.2;

    /// <summary>Share of clearly blue or green pixels above which a photo is colour, however narrow its spread.</summary>
    internal const double MaxMonochromeCoolShare = 0.01;

    /// <summary>Average tint above which a black-and-white print counts as sepia.</summary>
    internal const double MinSepiaTint = 8.0;

    /// <summary>Share of a colour photo's colourfulness that is one cast, from which it counts as faded.</summary>
    internal const double MinFadedCastShare = 0.75;

    /// <summary>
    /// The preset that suits a kind of print (see <see cref="Workspace.RestorationSettings.FromPresetName"/>): black
    /// and white made neutral, sepia keeping its tone, faded colour restored, and good colour only cleaned of dust.
    /// Null when the style isn't known.
    /// </summary>
    public static string? SuggestedPreset(PhotoStyle style) => style switch
    {
        PhotoStyle.BlackAndWhite => "bw",
        PhotoStyle.Sepia => "sepia",
        PhotoStyle.FadedColor => "faded",
        PhotoStyle.Color => "dust",
        _ => null
    };

    /// <summary>Reads an image file and classifies it; <see cref="PhotoStyle.Unknown"/> if it can't be read.</summary>
    public static PhotoStyle ClassifyFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        // A full read shrunk smoothly: OpenCV's reduced reads skip pixels, and the noise looks like colour.
        using Mat image = Cv2.ImRead(path, ImreadModes.Color);
        return Classify(image);
    }

    public static PhotoStyle Classify(Mat image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Empty()) return PhotoStyle.Unknown;
        if (image.Channels() == 1) return PhotoStyle.BlackAndWhite;

        using Mat bgr = ImageIo.NormalizeToBgr(image);
        using Mat small = new();
        double scale = Math.Min(1.0, 256.0 / Math.Max(bgr.Width, bgr.Height));
        Cv2.Resize(bgr, small, new Size(Math.Max(1, (int)(bgr.Width * scale)), Math.Max(1, (int)(bgr.Height * scale))), 0, 0, InterpolationFlags.Area);

        using Mat lab = new();
        Cv2.CvtColor(small, lab, ColorConversionCodes.BGR2Lab);
        using var channels = MatChannels.Split(lab);
        using Mat a = new();
        using Mat b = new();
        channels[1].ConvertTo(a, MatType.CV_32F, 1, -128);
        channels[2].ConvertTo(b, MatType.CV_32F, 1, -128);
        a.GetArray(out float[] av);
        b.GetArray(out float[] bv);
        int n = av.Length;
        if (n == 0) return PhotoStyle.Unknown;

        double meanA = 0, meanB = 0, chroma = 0;
        int cool = 0;
        for (int i = 0; i < n; i++)
        {
            meanA += av[i];
            meanB += bv[i];
            double c = Math.Sqrt(av[i] * av[i] + bv[i] * bv[i]);
            chroma += c;
            if (c > 8 && (av[i] < -3 || bv[i] < -3)) cool++;
        }
        meanA /= n;
        meanB /= n;
        chroma /= n;

        // Spread of the (a, b) cloud: across its main direction is the smaller eigenvalue of its covariance.
        double saa = 0, sbb = 0, sab = 0;
        for (int i = 0; i < n; i++)
        {
            double x = av[i] - meanA, y = bv[i] - meanB;
            saa += x * x;
            sbb += y * y;
            sab += x * y;
        }
        saa /= n;
        sbb /= n;
        sab /= n;
        double half = (saa + sbb) / 2;
        double crossSpread = Math.Sqrt(Math.Max(0, half - Math.Sqrt(Math.Max(0, half * half - (saa * sbb - sab * sab)))));

        double tint = Math.Sqrt(meanA * meanA + meanB * meanB);
        bool monochrome = PhotoRestorationService.IsMonochrome(bgr)
            || (crossSpread <= MaxMonochromeCrossSpread && (double)cool / n < MaxMonochromeCoolShare);

        if (monochrome)
        {
            return tint > MinSepiaTint && meanB > 0 ? PhotoStyle.Sepia : PhotoStyle.BlackAndWhite;
        }
        return chroma > 0 && tint / chroma >= MinFadedCastShare ? PhotoStyle.FadedColor : PhotoStyle.Color;
    }
}
