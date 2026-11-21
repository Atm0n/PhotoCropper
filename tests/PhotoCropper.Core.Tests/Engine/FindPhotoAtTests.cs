using PhotoCropper.Core.Workspace;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Core.Tests.Engine;

/// <summary>The "Find photo" tool: a click finds a missed photo, or re-fits the box it lands in.</summary>
public sealed class FindPhotoAtTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _scanPath;

    // CreateStandardTwoPhotoScan: a black 400x400 square at (100, 100), so centred at (300, 300).
    private const float SquareCentre = 300;
    private const float SquareSide = 400;

    public FindPhotoAtTests()
    {
        _tempDir = TestImageFactory.CreateTempDirectory("FindPhotoAtTests");
        _scanPath = Path.Combine(_tempDir, "scan.png");
        TestImageFactory.CreateStandardTwoPhotoScan(_scanPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch (IOException) { }
    }

    [Fact]
    public void ClickOnAMissedPhoto_AddsItsBox()
    {
        using var engine = new PhotoCropperEngine(_scanPath) { AutoOrientPhotos = false };
        engine.DetectPhotos();
        int square = IndexNear(engine, SquareCentre, SquareCentre);
        engine.DeletePhoto(square);
        int before = engine.DetectedPhotos.Count;

        var (index, added) = engine.FindPhotoAt(new System.Drawing.PointF(SquareCentre, SquareCentre));

        added.ShouldBeTrue();
        engine.DetectedPhotos.Count.ShouldBe(before + 1);
        engine.AcceptedCandidates.Count.ShouldBe(engine.DetectedPhotos.Count);
        ShouldBeTheSquare(engine.AcceptedCandidates[index].Rotated);
    }

    [Fact]
    public void ClickInsideALooseBox_FitsItToThePhoto()
    {
        using var engine = new PhotoCropperEngine(_scanPath) { AutoOrientPhotos = false };
        engine.RestoreFromSavedCrops([new WorkspaceCropData { CenterX = 310, CenterY = 290, Width = 480, Height = 470 }]);

        var (index, added) = engine.FindPhotoAt(new System.Drawing.PointF(SquareCentre, SquareCentre));

        added.ShouldBeFalse();
        index.ShouldBe(0);
        engine.DetectedPhotos.Count.ShouldBe(1);
        ShouldBeTheSquare(engine.AcceptedCandidates[0].Rotated);
        Math.Abs(engine.DetectedPhotos[0].Width - SquareSide).ShouldBeLessThan(12);
    }

    [Fact]
    public void ClickOnTheBackground_ChangesNothing()
    {
        using var engine = new PhotoCropperEngine(_scanPath) { AutoOrientPhotos = false };
        engine.DetectPhotos();
        int before = engine.DetectedPhotos.Count;

        var (index, _) = engine.FindPhotoAt(new System.Drawing.PointF(1800, 200));

        index.ShouldBe(-1);
        engine.DetectedPhotos.Count.ShouldBe(before);
    }

    // Album pages: the page itself is a clean rectangle around the click, so a plain search finds the page; the photo
    // only stands out against the page's paper. Photo 0 reaches the page's top and right edges.
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void ClickOnAPhotoOnAnAlbumPage_FindsThatPhotoNotThePage(int photo, bool othersBoxed)
    {
        var album = SampleScanFactory.CreateAlbumPage(Path.Combine(_tempDir, "album.png"), seed: 40);
        using var engine = new PhotoCropperEngine(album.Path) { AutoOrientPhotos = false };
        engine.RestoreFromSavedCrops(othersBoxed
            ? [.. album.Photos.Where((_, i) => i != photo).Select(BoxOf)]
            : []);
        var target = album.Photos[photo];

        var (index, added) = engine.FindPhotoAt(new System.Drawing.PointF(target.X + target.Width / 2f, target.Y + target.Height / 2f));

        added.ShouldBeTrue();
        Overlap(engine.AcceptedCandidates[index].Rotated, target).ShouldBeGreaterThan(0.9);
    }

    // A print whose white border is the bed's colour, stuck on torn album paper: against the bed only the picture
    // stands out; against the paper the whole print does, border included. Several generated prints: one alone
    // passed by luck while most of them lost their border.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void ClickOnAWhiteBorderedPrintOnPaper_IncludesItsBorder(int seed)
    {
        var scan = SampleScanFactory.CreatePrintOnPaper(Path.Combine(_tempDir, $"print{seed}.png"), seed: seed);
        using var engine = new PhotoCropperEngine(scan.Path) { AutoOrientPhotos = false };
        engine.RestoreFromSavedCrops([]);
        var print = scan.Photos[0];

        var (index, added) = engine.FindPhotoAt(new System.Drawing.PointF(print.X + print.Width / 2f, print.Y + print.Height / 2f));

        added.ShouldBeTrue();
        Overlap(engine.AcceptedCandidates[index].Rotated, print).ShouldBeGreaterThan(0.9);
    }

    private static WorkspaceCropData BoxOf(OpenCvSharp.Rect r) =>
        new() { CenterX = r.X + r.Width / 2f, CenterY = r.Y + r.Height / 2f, Width = r.Width, Height = r.Height };

    /// <summary>Intersection over union of a found (near-straight) box and the expected rectangle.</summary>
    private static double Overlap(Models.RotatedBox box, OpenCvSharp.Rect expected)
    {
        var v = box.GetVertices();
        float x0 = v.Min(p => p.X), y0 = v.Min(p => p.Y), x1 = v.Max(p => p.X), y1 = v.Max(p => p.Y);
        double ix = Math.Max(0, Math.Min(x1, expected.Right) - Math.Max(x0, expected.X));
        double iy = Math.Max(0, Math.Min(y1, expected.Bottom) - Math.Max(y0, expected.Y));
        double inter = ix * iy;
        return inter / ((x1 - x0) * (y1 - y0) + (double)expected.Width * expected.Height - inter);
    }

    private static int IndexNear(PhotoCropperEngine engine, float x, float y)
    {
        for (int i = 0; i < engine.AcceptedCandidates.Count; i++)
        {
            var c = engine.AcceptedCandidates[i].Rotated.Center;
            if (Math.Abs(c.X - x) < 30 && Math.Abs(c.Y - y) < 30) return i;
        }
        throw new InvalidOperationException("The square wasn't detected.");
    }

    private static void ShouldBeTheSquare(Models.RotatedBox box)
    {
        box.Center.X.ShouldBe(SquareCentre, 8);
        box.Center.Y.ShouldBe(SquareCentre, 8);
        box.Size.Width.ShouldBe(SquareSide, 12);
        box.Size.Height.ShouldBe(SquareSide, 12);
    }
}
