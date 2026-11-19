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
