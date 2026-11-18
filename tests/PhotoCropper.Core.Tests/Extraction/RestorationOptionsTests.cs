using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Core.Extraction;
using PhotoCropper.Core.Workspace;
using PhotoCropper.TestHelpers;
using System.Drawing;
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

        var mat = new Mat(height, width, DepthType.Cv8U, 3);
        mat.SetTo(data);
        return mat;
    }

    /// <summary>Two clearly different colours side by side.</summary>
    private static Mat TwoColours(MCvScalar left, MCvScalar right)
    {
        var mat = new Mat(120, 160, DepthType.Cv8U, 3);
        using (var l = new Mat(mat, new Rectangle(0, 0, 80, 120))) l.SetTo(left);
        using (var r = new Mat(mat, new Rectangle(80, 0, 80, 120))) r.SetTo(right);
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

        var mat = new Mat(height, width, DepthType.Cv8U, 3);
        mat.SetTo(data);
        return mat;
    }

    /// <summary>How far the pixels at a spot are from the background value after restoration.</summary>
    private static double Remaining(Mat image, Rectangle spot, double background)
    {
        using var region = new Mat(image, spot);
        return Math.Abs(CvInvoke.Mean(region).V0 - background);
    }

    private static MCvScalar MeanLab(Mat bgr)
    {
        using Mat lab = new();
        CvInvoke.CvtColor(bgr, lab, ColorConversion.Bgr2Lab);
        return CvInvoke.Mean(lab);
    }

    private static double Difference(Mat a, Mat b) => CvInvoke.Norm(a, b, NormType.L1);

    [Fact]
    public void IsMonochrome_ShouldRecogniseGreyAndSepiaButNotColour()
    {
        using var grey = TonedGradient(1, 1, 1);
        using var sepia = TonedGradient(0.75, 0.9, 1.0);
        using var colour = TwoColours(new MCvScalar(200, 60, 40), new MCvScalar(40, 160, 200));
        using var fadedColour = TwoColours(new MCvScalar(150, 130, 120), new MCvScalar(110, 130, 150));

        PhotoRestorationService.IsMonochrome(grey).ShouldBeTrue();
        PhotoRestorationService.IsMonochrome(sepia).ShouldBeTrue();
        PhotoRestorationService.IsMonochrome(colour).ShouldBeFalse();
        PhotoRestorationService.IsMonochrome(fadedColour).ShouldBeFalse("a faded colour print still has distinct colours");
    }

    [Fact]
    public void Restore_WithNothingToChange_ShouldReturnTheSamePhoto()
    {
        using var photo = TestImageFactory.CreateFadedPhoto();

        PhotoRestorationService.Restore(photo, RestorationSettings.None).ShouldBeSameAs(photo);
    }

    [Fact]
    public void Restore_DefaultSettings_ShouldMatchTheLegacyRestoreColorsCall()
    {
        using var photo = TestImageFactory.CreateFadedPhoto();

        using var legacy = PhotoRestorationService.RestoreColors(photo);
        using var settings = PhotoRestorationService.Restore(photo, new RestorationSettings { RestoreColors = true });

        Difference(legacy, settings).ShouldBe(0);
    }

    [Fact]
    public void Restore_NeutralBlackAndWhite_ShouldProduceGreyPixels()
    {
        using var sepia = TonedGradient(0.75, 0.9, 1.0);

        using var result = PhotoRestorationService.Restore(sepia, RestorationSettings.BlackAndWhite);

        using var channels = new Emgu.CV.Util.VectorOfMat();
        CvInvoke.Split(result, channels);
        Difference(channels[0], channels[1]).ShouldBe(0);
        Difference(channels[1], channels[2]).ShouldBe(0);
    }

    [Fact]
    public void Restore_SepiaPreset_ShouldKeepTheWarmTone()
    {
        using var sepia = TonedGradient(0.75, 0.9, 1.0);
        double warmthBefore = MeanLab(sepia).V2;

        using var result = PhotoRestorationService.Restore(sepia, RestorationSettings.Sepia);

        // LAB b > 128 is yellow/warm; white balance would have pulled it to neutral (128).
        MeanLab(result).V2.ShouldBeGreaterThan(128 + (warmthBefore - 128) * 0.8);
    }

    [Fact]
    public void Restore_AutoColorMode_ShouldNotNeutraliseADetectedSepiaPrint()
    {
        using var sepia = TonedGradient(0.75, 0.9, 1.0);
        double warmthBefore = MeanLab(sepia).V2;

        using var auto = PhotoRestorationService.Restore(sepia, new RestorationSettings { RestoreColors = true });
        using var forcedColour = PhotoRestorationService.Restore(sepia, new RestorationSettings { RestoreColors = true, ColorMode = PhotoColorMode.Color });

        MeanLab(auto).V2.ShouldBeGreaterThan(MeanLab(forcedColour).V2, "treated as colour, white balance removes the tone");
        MeanLab(auto).V2.ShouldBeGreaterThan(128 + (warmthBefore - 128) * 0.8);
    }

    [Fact]
    public void Restore_StrongerStrength_ShouldChangeThePhotoMore()
    {
        using var photo = FadedColourPrint();

        using var gentle = PhotoRestorationService.Restore(photo, new RestorationSettings { RestoreColors = true, Strength = RestorationStrength.Gentle });
        using var strong = PhotoRestorationService.Restore(photo, new RestorationSettings { RestoreColors = true, Strength = RestorationStrength.Strong });

        Difference(strong, photo).ShouldBeGreaterThan(Difference(gentle, photo));
    }

    [Fact]
    public void Restore_WithoutColourCastRemoval_ShouldKeepTheYellowCast()
    {
        using var photo = FadedColourPrint();
        double castBefore = MeanLab(photo).V2 - 128; // > 0: yellow

        using var all = PhotoRestorationService.Restore(photo, new RestorationSettings { RestoreColors = true, ColorMode = PhotoColorMode.Color });
        using var noCastRemoval = PhotoRestorationService.Restore(photo, new RestorationSettings
        {
            RestoreColors = true, ColorMode = PhotoColorMode.Color, AutoWhiteBalance = false
        });

        castBefore.ShouldBeGreaterThan(5);
        (MeanLab(all).V2 - 128).ShouldBeLessThan(castBefore, "colour cast removal reduces the yellow");
        (MeanLab(noCastRemoval).V2 - 128).ShouldBeGreaterThan(MeanLab(all).V2 - 128);
    }

    [Theory]
    [InlineData(50, 0, 0, 0, 0)]
    [InlineData(0, 0, 60, 0, 0)]
    [InlineData(0, 0, 0, 60, 0)]
    public void Restore_ManualAdjustments_ShouldMoveTheMatchingChannel(int brightness, int contrast, int warmth, int tint, int saturation)
    {
        using var photo = TwoColours(new MCvScalar(120, 120, 120), new MCvScalar(90, 110, 130));
        var before = MeanLab(photo);

        using var result = PhotoRestorationService.Restore(photo, new RestorationSettings
        {
            Brightness = brightness, Contrast = contrast, Warmth = warmth, Tint = tint, Saturation = saturation
        });
        var after = MeanLab(result);

        if (brightness > 0) after.V0.ShouldBeGreaterThan(before.V0);
        if (warmth > 0) after.V2.ShouldBeGreaterThan(before.V2);
        if (tint > 0) after.V1.ShouldBeGreaterThan(before.V1);
    }

    [Fact]
    public void Restore_SaturationMinus100_ShouldRemoveColour()
    {
        using var photo = TwoColours(new MCvScalar(200, 60, 40), new MCvScalar(40, 160, 200));

        using var result = PhotoRestorationService.Restore(photo, new RestorationSettings { Saturation = -100 });

        using Mat hsv = new();
        CvInvoke.CvtColor(result, hsv, ColorConversion.Bgr2Hsv);
        CvInvoke.Mean(hsv).V1.ShouldBeLessThan(2);
    }

    [Theory]
    [InlineData(255, 1)] // bright speck
    [InlineData(255, 2)]
    [InlineData(0, 1)]   // dark speck
    [InlineData(80, 1)]  // faint speck
    public void RemoveDust_ShouldRemoveSpecksOnCleanBackground(int speck, int radius)
    {
        // Regression: the edge guard used to cancel every speck, because a speck's own outline is an edge.
        using var photo = new Mat(200, 200, DepthType.Cv8U, 3);
        photo.SetTo(new MCvScalar(128, 128, 128));
        CvInvoke.Circle(photo, new Point(100, 100), radius, new MCvScalar(speck, speck, speck), -1);

        using var result = PhotoRestorationService.InpaintDustAndScratches(photo);

        Remaining(result, new Rectangle(100 - radius, 100 - radius, 2 * radius + 1, 2 * radius + 1), 128).ShouldBeLessThan(3);
    }

    [Fact]
    public void RemoveDust_OnAHighResolutionScan_ShouldRemoveLargerSpecks()
    {
        using var photo = new Mat(1600, 1600, DepthType.Cv8U, 3);
        photo.SetTo(new MCvScalar(128, 128, 128));
        CvInvoke.Circle(photo, new Point(800, 800), 3, new MCvScalar(0, 0, 0), -1); // 7 px speck

        using var result = PhotoRestorationService.InpaintDustAndScratches(photo);

        Remaining(result, new Rectangle(797, 797, 7, 7), 128).ShouldBeLessThan(3);
    }

    [Fact]
    public void RemoveDust_ShouldLeaveNaturalEdgesAndLinesUntouched()
    {
        using var photo = new Mat(200, 200, DepthType.Cv8U, 3);
        photo.SetTo(new MCvScalar(200, 200, 200));
        CvInvoke.Rectangle(photo, new Rectangle(60, 60, 80, 80), new MCvScalar(30, 30, 30), -1);
        CvInvoke.Line(photo, new Point(10, 20), new Point(190, 20), new MCvScalar(40, 40, 40), 1);

        using var result = PhotoRestorationService.InpaintDustAndScratches(photo);

        Difference(result, photo).ShouldBe(0);
    }

    [Fact]
    public void Restore_OnlyCleanDust_ShouldRemoveDustWithoutTouchingColours()
    {
        using var photo = TestImageFactory.CreateBlemishedPhoto();

        using var result = PhotoRestorationService.Restore(photo, RestorationSettings.OnlyCleanDust);

        Remaining(result, new Rectangle(48, 48, 5, 5), 128).ShouldBeLessThan(3, "the bright speck is removed");
        Remaining(result, new Rectangle(10, 150, 30, 30), 128).ShouldBe(0, "the rest of the photo keeps its colours");
    }

    [Theory]
    [InlineData("faded")]
    [InlineData("BW")]
    [InlineData("sepia")]
    [InlineData("dust")]
    [InlineData("none")]
    public void FromPresetName_ShouldKnowEveryPreset(string name)
    {
        RestorationSettings.FromPresetName(name).ShouldNotBeNull();
    }

    [Fact]
    public void SettingsSavedByOlderVersions_ShouldLoadWithDefaultsForNewOptions()
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
