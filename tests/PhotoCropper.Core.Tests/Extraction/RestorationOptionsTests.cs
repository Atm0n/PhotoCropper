using OpenCvSharp;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Extraction;
using PhotoCropper.Core.Workspace;
using PhotoCropper.TestHelpers;
using System.Text.Json;

namespace PhotoCropper.Core.Tests.Extraction;

public sealed class RestorationOptionsTests
{
    /// <summary>A horizontal gradient in one tone: grey (b,g,r equal) or tinted (e.g. sepia).</summary>
    private static Mat TonedGradient(double bScale, double gScale, double rScale)
    {
        const int width = 160, height = 120;
        var data = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double v = 40 + x * 1.2;
                int i = (y * width + x) * 3;
                data[i] = (byte)Math.Round(v * bScale);
                data[i + 1] = (byte)Math.Round(v * gScale);
                data[i + 2] = (byte)Math.Round(v * rScale);
            }
        }

        var mat = new Mat(height, width, MatType.CV_8UC3);
        mat.SetData(data);
        return mat;
    }

    /// <summary>Two clearly different colours side by side.</summary>
    private static Mat TwoColours(Scalar left, Scalar right)
    {
        var mat = new Mat(120, 160, MatType.CV_8UC3);
        using (var l = new Mat(mat, new Rect(0, 0, 80, 120))) l.SetTo(left);
        using (var r = new Mat(mat, new Rect(80, 0, 80, 120))) r.SetTo(right);
        return mat;
    }

    /// <summary>A realistic faded colour print: low-contrast gradients with a yellow cast and several hues.</summary>
    private static Mat FadedColourPrint()
    {
        const int width = 200, height = 160;
        var data = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double light = 110 + x * 0.25;       // compressed range: 110..160
                double hue = (y - height / 2) * 0.25; // bluish at the top, reddish at the bottom
                int i = (y * width + x) * 3;
                data[i] = (byte)Math.Clamp(light - 25 - hue, 0, 255);   // B low: yellow cast
                data[i + 1] = (byte)Math.Clamp(light, 0, 255);
                data[i + 2] = (byte)Math.Clamp(light + 8 + hue, 0, 255);
            }
        }

        var mat = new Mat(height, width, MatType.CV_8UC3);
        mat.SetData(data);
        return mat;
    }

    /// <summary>How far the pixels at a spot are from the background value after restoration.</summary>
    private static double Remaining(Mat image, Rect spot, double background)
    {
        using var region = new Mat(image, spot);
        return Math.Abs(Cv2.Mean(region).Val0 - background);
    }

    private static Scalar MeanLab(Mat bgr)
    {
        using Mat lab = new();
        Cv2.CvtColor(bgr, lab, ColorConversionCodes.BGR2Lab);
        return Cv2.Mean(lab);
    }

    private static double Difference(Mat a, Mat b) => Cv2.Norm(a, b, NormTypes.L1);

    [Fact]
    public void IsMonochromeShouldRecogniseGreyAndSepiaButNotColour()
    {
        using var grey = TonedGradient(1, 1, 1);
        using var sepia = TonedGradient(0.75, 0.9, 1.0);
        using var colour = TwoColours(new Scalar(200, 60, 40), new Scalar(40, 160, 200));
        using var fadedColour = TwoColours(new Scalar(150, 130, 120), new Scalar(110, 130, 150));

        PhotoRestorationService.IsMonochrome(grey).ShouldBeTrue();
        PhotoRestorationService.IsMonochrome(sepia).ShouldBeTrue();
        PhotoRestorationService.IsMonochrome(colour).ShouldBeFalse();
        PhotoRestorationService.IsMonochrome(fadedColour).ShouldBeFalse("a faded colour print still has distinct colours");
    }

    [Fact]
    public void RestoreWithNothingToChangeShouldReturnTheSamePhoto()
    {
        using var photo = TestImageFactory.CreateFadedPhoto();

        PhotoRestorationService.Restore(photo, RestorationSettings.None).ShouldBeSameAs(photo);
    }

    [Fact]
    public void RestoreDefaultSettingsShouldMatchTheLegacyRestoreColorsCall()
    {
        using var photo = TestImageFactory.CreateFadedPhoto();

        using var legacy = PhotoRestorationService.RestoreColors(photo);
        using var settings = PhotoRestorationService.Restore(photo, new RestorationSettings { RestoreColors = true });

        Difference(legacy, settings).ShouldBe(0);
    }

    [Fact]
    public void RestoreNeutralBlackAndWhiteShouldProduceGreyPixels()
    {
        using var sepia = TonedGradient(0.75, 0.9, 1.0);

        using var result = PhotoRestorationService.Restore(sepia, RestorationSettings.BlackAndWhite);

        using var channels = MatChannels.Split(result);
        Difference(channels[0], channels[1]).ShouldBe(0);
        Difference(channels[1], channels[2]).ShouldBe(0);
    }

    [Fact]
    public void RestoreSepiaPresetShouldKeepTheWarmTone()
    {
        using var sepia = TonedGradient(0.75, 0.9, 1.0);
        double warmthBefore = MeanLab(sepia).Val2;

        using var result = PhotoRestorationService.Restore(sepia, RestorationSettings.Sepia);

        // LAB b > 128 is yellow/warm; white balance would have pulled it to neutral (128).
        MeanLab(result).Val2.ShouldBeGreaterThan(128 + (warmthBefore - 128) * 0.8);
    }

    [Fact]
    public void RestoreAutoColorModeShouldNotNeutraliseADetectedSepiaPrint()
    {
        using var sepia = TonedGradient(0.75, 0.9, 1.0);
        double warmthBefore = MeanLab(sepia).Val2;

        using var auto = PhotoRestorationService.Restore(sepia, new RestorationSettings { RestoreColors = true });
        using var forcedColour = PhotoRestorationService.Restore(sepia, new RestorationSettings { RestoreColors = true, ColorMode = PhotoColorMode.Color });

        MeanLab(auto).Val2.ShouldBeGreaterThan(MeanLab(forcedColour).Val2, "treated as colour, white balance removes the tone");
        MeanLab(auto).Val2.ShouldBeGreaterThan(128 + (warmthBefore - 128) * 0.8);
    }

    [Fact]
    public void RestoreStrongerStrengthShouldChangeThePhotoMore()
    {
        using var photo = FadedColourPrint();

        using var gentle = PhotoRestorationService.Restore(photo, new RestorationSettings { RestoreColors = true, Strength = RestorationStrength.Gentle });
        using var strong = PhotoRestorationService.Restore(photo, new RestorationSettings { RestoreColors = true, Strength = RestorationStrength.Strong });

        Difference(strong, photo).ShouldBeGreaterThan(Difference(gentle, photo));
    }

    [Fact]
    public void RestoreWithoutColourCastRemovalShouldKeepTheYellowCast()
    {
        using var photo = FadedColourPrint();
        double castBefore = MeanLab(photo).Val2 - 128; // > 0: yellow

        using var all = PhotoRestorationService.Restore(photo, new RestorationSettings { RestoreColors = true, ColorMode = PhotoColorMode.Color });
        using var noCastRemoval = PhotoRestorationService.Restore(photo, new RestorationSettings
        {
            RestoreColors = true, ColorMode = PhotoColorMode.Color, AutoWhiteBalance = false
        });

        castBefore.ShouldBeGreaterThan(5);
        (MeanLab(all).Val2 - 128).ShouldBeLessThan(castBefore, "colour cast removal reduces the yellow");
        (MeanLab(noCastRemoval).Val2 - 128).ShouldBeGreaterThan(MeanLab(all).Val2 - 128);
    }

    [Theory]
    [InlineData(50, 0, 0, 0, 0)]
    [InlineData(0, 0, 60, 0, 0)]
    [InlineData(0, 0, 0, 60, 0)]
    public void RestoreManualAdjustmentsShouldMoveTheMatchingChannel(int brightness, int contrast, int warmth, int tint, int saturation)
    {
        using var photo = TwoColours(new Scalar(120, 120, 120), new Scalar(90, 110, 130));
        var before = MeanLab(photo);

        using var result = PhotoRestorationService.Restore(photo, new RestorationSettings
        {
            Brightness = brightness, Contrast = contrast, Warmth = warmth, Tint = tint, Saturation = saturation
        });
        var after = MeanLab(result);

        if (brightness > 0) after.Val0.ShouldBeGreaterThan(before.Val0);
        if (warmth > 0) after.Val2.ShouldBeGreaterThan(before.Val2);
        if (tint > 0) after.Val1.ShouldBeGreaterThan(before.Val1);
    }

    [Fact]
    public void RestoreSaturationMinus100ShouldRemoveColour()
    {
        using var photo = TwoColours(new Scalar(200, 60, 40), new Scalar(40, 160, 200));

        using var result = PhotoRestorationService.Restore(photo, new RestorationSettings { Saturation = -100 });

        using Mat hsv = new();
        Cv2.CvtColor(result, hsv, ColorConversionCodes.BGR2HSV);
        Cv2.Mean(hsv).Val1.ShouldBeLessThan(2);
    }

    [Theory]
    [InlineData(255, 1)] // bright speck
    [InlineData(255, 2)]
    [InlineData(0, 1)]   // dark speck
    [InlineData(80, 1)]  // faint speck
    public void RemoveDustShouldRemoveSpecksOnCleanBackground(int speck, int radius)
    {
        // Regression: the edge guard used to cancel every speck, because a speck's own outline is an edge.
        using var photo = new Mat(200, 200, MatType.CV_8UC3);
        photo.SetTo(new Scalar(128, 128, 128));
        Cv2.Circle(photo, new Point(100, 100), radius, new Scalar(speck, speck, speck), -1);

        using var result = PhotoRestorationService.InpaintDustAndScratches(photo);

        Remaining(result, new Rect(100 - radius, 100 - radius, 2 * radius + 1, 2 * radius + 1), 128).ShouldBeLessThan(3);
    }

    [Fact]
    public void RemoveDustOnAHighResolutionScanShouldRemoveLargerSpecks()
    {
        using var photo = new Mat(1600, 1600, MatType.CV_8UC3);
        photo.SetTo(new Scalar(128, 128, 128));
        Cv2.Circle(photo, new Point(800, 800), 3, new Scalar(0, 0, 0), -1); // 7 px speck

        using var result = PhotoRestorationService.InpaintDustAndScratches(photo);

        Remaining(result, new Rect(797, 797, 7, 7), 128).ShouldBeLessThan(3);
    }

    [Fact]
    public void RemoveDustShouldLeaveNaturalEdgesAndLinesUntouched()
    {
        using var photo = new Mat(200, 200, MatType.CV_8UC3);
        photo.SetTo(new Scalar(200, 200, 200));
        Cv2.Rectangle(photo, new Rect(60, 60, 80, 80), new Scalar(30, 30, 30), -1);
        Cv2.Line(photo, new Point(10, 20), new Point(190, 20), new Scalar(40, 40, 40), 1);

        using var result = PhotoRestorationService.InpaintDustAndScratches(photo);

        Difference(result, photo).ShouldBe(0);
    }

    [Fact]
    public void RestoreOnlyCleanDustShouldRemoveDustWithoutTouchingColours()
    {
        using var photo = TestImageFactory.CreateBlemishedPhoto();

        using var result = PhotoRestorationService.Restore(photo, RestorationSettings.OnlyCleanDust);

        Remaining(result, new Rect(48, 48, 5, 5), 128).ShouldBeLessThan(3, "the bright speck is removed");
        Remaining(result, new Rect(10, 150, 30, 30), 128).ShouldBe(0, "the rest of the photo keeps its colours");
    }

    [Theory]
    [InlineData("faded")]
    [InlineData("BW")]
    [InlineData("sepia")]
    [InlineData("dust")]
    [InlineData("none")]
    public void FromPresetNameShouldKnowEveryPreset(string name)
    {
        RestorationSettings.FromPresetName(name).ShouldNotBeNull();
    }

    [Fact]
    public void SettingsSavedByOlderVersionsShouldLoadWithDefaultsForNewOptions()
    {
        var loaded = JsonSerializer.Deserialize<RestorationSettings>("""{ "RestoreColors": true, "RemoveDust": true }""")!;

        loaded.Strength.ShouldBe(RestorationStrength.Normal);
        loaded.AutoWhiteBalance.ShouldBeTrue();
        loaded.AutoContrast.ShouldBeTrue();
        loaded.AutoSaturation.ShouldBeTrue();
        loaded.ColorMode.ShouldBe(PhotoColorMode.Auto);
        loaded.HasManualAdjustments.ShouldBeFalse();
    }
}
