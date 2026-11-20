using OpenCvSharp;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Export;
using PhotoCropper.Core.Extraction;
using PhotoCropper.Core.Workspace;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Core.Tests.Extraction;

/// <summary>"Trim white borders": finding the picture inside a print's white border, and only when there is one.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Test images: a seeded, repeatable generator is the point")]
public sealed class WhiteBorderTests : IDisposable
{
    private readonly string _tempDir = TestImageFactory.CreateTempDirectory("WhiteBorderTests");

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch (IOException) { }
    }

    /// <summary>Photo-like content: colourful, mid-tone, never white at its corners.</summary>
    private static Mat Picture(int width, int height, int seed)
    {
        var random = new Random(seed); // NOSONAR: reproducible test images from a fixed seed, not security
        byte[] noise = new byte[width * height * 3];
        random.NextBytes(noise);
        var picture = new Mat(height, width, MatType.CV_8UC3);
        picture.SetData(noise);
        Cv2.GaussianBlur(picture, picture, new Size(0, 0), 6);
        Cv2.Normalize(picture, picture, 30, 190, NormTypes.MinMax);
        return picture;
    }

    /// <summary>A print: <paramref name="picture"/> on a white border of the given widths.</summary>
    private static Mat Print(Mat picture, int left, int top, int right, int bottom)
    {
        var print = new Mat(picture.Height + top + bottom, picture.Width + left + right, MatType.CV_8UC3, new Scalar(238, 240, 242));
        using var target = new Mat(print, new Rect(left, top, picture.Width, picture.Height));
        picture.CopyTo(target);
        return print;
    }

    private static void ShouldBeNear(Rect actual, Rect expected, int tolerance = 5)
    {
        actual.X.ShouldBeInRange(expected.X - tolerance, expected.X + tolerance);
        actual.Y.ShouldBeInRange(expected.Y - tolerance, expected.Y + tolerance);
        actual.Right.ShouldBeInRange(expected.Right - tolerance, expected.Right + tolerance);
        actual.Bottom.ShouldBeInRange(expected.Bottom - tolerance, expected.Bottom + tolerance);
    }

    [Fact]
    public void GetWhiteBorderRect_PrintWithABorder_ShouldBeThePictureInside()
    {
        using var picture = Picture(400, 300, 1);
        using var print = Print(picture, 24, 24, 24, 24);

        var rect = EdgeRefinementService.GetWhiteBorderRect(print);

        rect.ShouldNotBeNull();
        ShouldBeNear(rect.Value, new Rect(24, 24, 400, 300));
    }

    [Fact]
    public void GetWhiteBorderRect_PolaroidWithAWideBottomEdge_ShouldLeaveItOut()
    {
        using var picture = Picture(300, 300, 2);
        // A Polaroid's proportions: the bottom edge is about a fifth of the print's height.
        using var print = Print(picture, 18, 18, 18, 72);

        var rect = EdgeRefinementService.GetWhiteBorderRect(print);

        rect.ShouldNotBeNull();
        ShouldBeNear(rect.Value, new Rect(18, 18, 300, 300));
    }

    /// <summary>A print lying on an album page: a band of pink page around it, wider on some sides, and a thin dark
    /// line (the print's edge and its shadow) between page and border.</summary>
    private static Mat OnAlbumPage(Mat print, int left, int top, int right, int bottom)
    {
        var page = new Mat(print.Height + top + bottom, print.Width + left + right, MatType.CV_8UC3, new Scalar(205, 214, 240));
        Cv2.Rectangle(page, new Rect(left - 2, top - 2, print.Width + 4, print.Height + 4), new Scalar(150, 155, 165), 2);
        using var target = new Mat(page, new Rect(left, top, print.Width, print.Height));
        print.CopyTo(target);
        return page;
    }

    [Fact]
    public void GetWhiteBorderRect_PrintOnAnAlbumPage_ShouldLeaveOutPageAndBorder()
    {
        using var picture = Picture(400, 300, 7);
        using var print = Print(picture, 20, 20, 20, 20);
        using var crop = OnAlbumPage(print, 12, 8, 60, 14);

        var rect = EdgeRefinementService.GetWhiteBorderRect(crop);

        rect.ShouldNotBeNull();
        ShouldBeNear(rect.Value, new Rect(12 + 20, 8 + 20, 400, 300));
    }

    [Fact]
    public void GetWhiteBorderRect_WornCorner_ShouldStillFindTheBorder()
    {
        using var picture = Picture(400, 300, 8);
        using var print = Print(picture, 24, 24, 24, 24);
        // A torn or rounded corner shows the dark scanner lid.
        Cv2.Rectangle(print, new Rect(0, 0, 14, 14), new Scalar(40, 40, 40), -1);

        var rect = EdgeRefinementService.GetWhiteBorderRect(print);

        rect.ShouldNotBeNull();
        ShouldBeNear(rect.Value, new Rect(24, 24, 400, 300));
    }

    [Fact]
    public void GetWhiteBorderRect_PaleBackgroundAlongOneEdge_ShouldBeNull()
    {
        // A portrait's pale studio background along the top, and only a light sliver of the scan on the other sides.
        using var picture = Picture(400, 300, 9);
        using (var sky = new Mat(picture, new Rect(0, 0, 400, 60))) sky.SetTo(new Scalar(232, 234, 236));
        using var crop = Print(picture, 4, 0, 4, 4);

        EdgeRefinementService.GetWhiteBorderRect(crop).ShouldBeNull();
    }

    [Fact]
    public void GetWhiteBorderRect_PhotoWithoutABorder_ShouldBeNull()
    {
        using var picture = Picture(400, 300, 3);

        EdgeRefinementService.GetWhiteBorderRect(picture).ShouldBeNull();
    }

    [Fact]
    public void GetWhiteBorderRect_BrightSkyAlongOneEdgeOnly_ShouldNotBeTakenForABorder()
    {
        // A photo whose top third is overcast sky: light at the top corners, dark at the bottom ones.
        using var photo = Picture(400, 300, 4);
        Cv2.Rectangle(photo, new Rect(0, 0, 400, 100), new Scalar(235, 235, 235), -1);

        EdgeRefinementService.GetWhiteBorderRect(photo).ShouldBeNull();
    }

    [Fact]
    public void GetWhiteBorderRect_VignetteFadingToWhite_ShouldNotBeTakenForABorder()
    {
        // A portrait whose edges fade gradually into white, as old studio portraits do: no clean edge, no border.
        using var photo = Picture(300, 400, 6);
        using var white = new Mat(photo.Size(), MatType.CV_8UC3, new Scalar(238, 240, 242));
        using var weight = new Mat(photo.Size(), MatType.CV_32FC1, Scalar.All(0));
        Cv2.Rectangle(weight, new Rect(90, 90, 120, 220), Scalar.All(1), -1);
        Cv2.GaussianBlur(weight, weight, new Size(0, 0), 45);
        using var photoF = new Mat(); using var whiteF = new Mat();
        photo.ConvertTo(photoF, MatType.CV_32FC3); white.ConvertTo(whiteF, MatType.CV_32FC3);
        using var w3 = new Mat();
        Cv2.Merge([weight, weight, weight], w3);
        using var inv = new Mat();
        Cv2.Subtract(Scalar.All(1), w3, inv);
        using var picturePart = new Mat(); using var whitePart = new Mat(); using var blended = new Mat();
        Cv2.Multiply(photoF, w3, picturePart);
        Cv2.Multiply(whiteF, inv, whitePart);
        Cv2.Add(picturePart, whitePart, blended);
        using var result = new Mat();
        blended.ConvertTo(result, MatType.CV_8UC3);

        EdgeRefinementService.GetWhiteBorderRect(result).ShouldBeNull();
    }

    // A dark bed with one bordered print: the print's 600x450 outside, its 540x390 picture 30 px in.
    private string BorderedPrintScan()
    {
        string path = Path.Combine(_tempDir, "bordered.png");
        using var scan = new Mat(1200, 1600, MatType.CV_8UC3, new Scalar(60, 60, 60));
        using var picture = Picture(540, 390, 5);
        using var print = Print(picture, 30, 30, 30, 30);
        using (var target = new Mat(scan, new Rect(400, 300, 600, 450))) print.CopyTo(target);
        Cv2.ImWrite(path, scan);
        return path;
    }

    private static Rect BoxBounds(PhotoCropperEngine engine, int index)
    {
        var v = engine.AcceptedCandidates[index].Rotated.GetVertices();
        int x0 = (int)Math.Round(v.Min(p => p.X)), y0 = (int)Math.Round(v.Min(p => p.Y));
        int x1 = (int)Math.Round(v.Max(p => p.X)), y1 = (int)Math.Round(v.Max(p => p.Y));
        return new Rect(x0, y0, x1 - x0, y1 - y0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Render_TrimsTheBorderOnlyWhenAsked(bool trim)
    {
        using var picture = Picture(400, 300, 7);
        using var print = Print(picture, 24, 24, 24, 24);

        using var rendered = RestoreStage.Render(print, RestorationSettings.None with { TrimWhiteBorders = trim });

        rendered.Width.ShouldBeInRange(trim ? 395 : 448, trim ? 400 : 448);
        rendered.Height.ShouldBeInRange(trim ? 295 : 348, trim ? 300 : 348);
    }

    [Fact]
    public void Render_TrimsAfterRestoringColours()
    {
        using var picture = Picture(400, 300, 8);
        using var print = Print(picture, 24, 24, 24, 24);
        var settings = RestorationSettings.BlackAndWhite with { TrimWhiteBorders = true };

        using var rendered = RestoreStage.Render(print, settings);

        rendered.Width.ShouldBeInRange(395, 400);
        // Black and white: no colour left in the picture.
        using var hsv = new Mat();
        Cv2.CvtColor(rendered, hsv, ColorConversionCodes.BGR2HSV);
        Cv2.Mean(hsv).Val1.ShouldBeLessThan(10);
    }

    [Fact]
    public void RenderKeepingBorder_ShowsTheWholePrintAndReportsThePicture()
    {
        using var picture = Picture(400, 300, 9);
        using var print = Print(picture, 24, 24, 24, 24);

        using var rendered = RestoreStage.RenderKeepingBorder(print, RestorationSettings.None with { TrimWhiteBorders = true }, null, out Rect? inside);

        rendered.Width.ShouldBe(448);
        inside.ShouldNotBeNull();
        ShouldBeNear(inside.Value, new Rect(24, 24, 400, 300));
    }

    [Fact]
    public void Presets_ShouldNotTurnTrimmingOn()
    {
        foreach (var name in new[] { "faded", "bw", "sepia", "dust", "none" })
        {
            RestorationSettings.FromPresetName(name)!.TrimWhiteBorders.ShouldBeFalse(name);
        }
        RestorationSettings.None.ChangesPhoto.ShouldBeFalse();
        (RestorationSettings.None with { TrimWhiteBorders = true }).ChangesPhoto.ShouldBeTrue();
    }

    // The CLI's one-step batch trims the extracted photos themselves (and their boxes).
    [Fact]
    public void Engine_TrimWhiteBorders_ShrinksPhotoAndBoxToThePicture()
    {
        using var engine = new PhotoCropperEngine(BorderedPrintScan()) { AutoOrientPhotos = false };
        engine.DetectPhotos();
        engine.DetectedPhotos.Count.ShouldBe(1);
        ShouldBeNear(BoxBounds(engine, 0), new Rect(400, 300, 600, 450), 8);

        engine.TrimWhiteBorders().ShouldBe(1);

        ShouldBeNear(BoxBounds(engine, 0), new Rect(430, 330, 540, 390), 8);
        engine.DetectedPhotos[0].Width.ShouldBeInRange(530, 550);
        engine.RawDetectedPhotos[0].Width.ShouldBe(engine.DetectedPhotos[0].Width);
    }
}
