using OpenCvSharp;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Extraction;
using PhotoCropper.Core.Workspace;

namespace PhotoCropper.Core.Tests.Extraction;

public class PhotoStyleClassifierTests
{
    /// <summary>A grey scene (a gradient with some shapes), each pixel's grey turned into a colour by <paramref name="tone"/>.</summary>
    private static Mat Monochrome(Func<double, Scalar> tone)
    {
        const int rows = 200, cols = 300;
        var pixels = new byte[rows * cols * 3];
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                double grey = 40 + 180.0 * x / cols;
                if (Math.Abs(x - 150) < 40 && Math.Abs(y - 100) < 50) grey = 255 - grey; // a "figure"
                Scalar c = tone(grey);
                int i = (y * cols + x) * 3;
                pixels[i] = (byte)Math.Clamp(c.Val0, 0, 255);
                pixels[i + 1] = (byte)Math.Clamp(c.Val1, 0, 255);
                pixels[i + 2] = (byte)Math.Clamp(c.Val2, 0, 255);
            }
        }
        var photo = new Mat(rows, cols, MatType.CV_8UC3);
        photo.SetData(pixels);
        return photo;
    }

    /// <summary>Blocks of many hues (sky, grass, clothes), each mixed with <paramref name="cast"/> by <paramref name="castShare"/>.</summary>
    private static Mat Colourful(Scalar cast, double castShare)
    {
        Scalar[] hues =
        [
            new(200, 120, 60), new(60, 160, 60), new(40, 40, 200), new(180, 180, 40),
            new(40, 200, 220), new(160, 60, 160), new(120, 120, 120), new(220, 200, 180),
        ];
        var photo = new Mat(200, 400, MatType.CV_8UC3);
        for (int i = 0; i < hues.Length; i++)
        {
            var h = hues[i];
            var mixed = new Scalar(
                h.Val0 * (1 - castShare) + cast.Val0 * castShare,
                h.Val1 * (1 - castShare) + cast.Val1 * castShare,
                h.Val2 * (1 - castShare) + cast.Val2 * castShare);
            using var block = new Mat(photo, new Rect(i % 4 * 100, i / 4 * 100, 100, 100));
            block.SetTo(mixed);
        }
        return photo;
    }

    [Fact]
    public void Classify_GreyPrint_ShouldBeBlackAndWhite()
    {
        using var photo = Monochrome(g => new Scalar(g, g, g));
        PhotoStyleClassifier.Classify(photo).ShouldBe(PhotoStyle.BlackAndWhite);
    }

    [Fact]
    public void Classify_SlightlyYellowedPrint_ShouldStillBeBlackAndWhite()
    {
        using var photo = Monochrome(g => new Scalar(g * 0.93, g * 0.97, g));
        PhotoStyleClassifier.Classify(photo).ShouldBe(PhotoStyle.BlackAndWhite);
    }

    [Fact]
    public void Classify_WarmTonedPrint_ShouldBeSepia()
    {
        using var photo = Monochrome(g => new Scalar(g * 0.62, g * 0.82, g));
        PhotoStyleClassifier.Classify(photo).ShouldBe(PhotoStyle.Sepia);
    }

    [Fact]
    public void Classify_ColourPhoto_ShouldBeColour()
    {
        using var photo = Colourful(new Scalar(0, 0, 0), 0);
        PhotoStyleClassifier.Classify(photo).ShouldBe(PhotoStyle.Color);
    }

    [Fact]
    public void Classify_ColourPrintFadedToRed_ShouldBeFadedColour()
    {
        using var photo = Colourful(new Scalar(110, 120, 220), 0.6);
        PhotoStyleClassifier.Classify(photo).ShouldBe(PhotoStyle.FadedColor);
    }

    [Fact]
    public void Classify_SingleChannelOrEmpty_ShouldNotThrow()
    {
        using var grey = new Mat(50, 50, MatType.CV_8UC1, Scalar.All(100));
        using var empty = new Mat();
        PhotoStyleClassifier.Classify(grey).ShouldBe(PhotoStyle.BlackAndWhite);
        PhotoStyleClassifier.Classify(empty).ShouldBe(PhotoStyle.Unknown);
    }

    [Theory]
    [InlineData(PhotoStyle.BlackAndWhite)]
    [InlineData(PhotoStyle.Sepia)]
    [InlineData(PhotoStyle.FadedColor)]
    [InlineData(PhotoStyle.Color)]
    public void SuggestedPreset_ShouldNameARealPreset(PhotoStyle style)
    {
        RestorationSettings.FromPresetName(PhotoStyleClassifier.SuggestedPreset(style)).ShouldNotBeNull();
        PhotoStyleClassifier.SuggestedPreset(PhotoStyle.Unknown).ShouldBeNull();
    }
}
