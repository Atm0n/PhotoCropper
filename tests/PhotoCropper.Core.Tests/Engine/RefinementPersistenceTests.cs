using PhotoCropper.Core.Models;
using PhotoCropper.Core.Workspace;
using PhotoCropper.TestHelpers;
using Point2f = System.Drawing.PointF;
using Rect = System.Drawing.Rectangle;
using RotatedRect = PhotoCropper.Core.Models.RotatedBox;
using Size = System.Drawing.Size;
using Size2f = System.Drawing.SizeF;

namespace PhotoCropper.Core.Tests.Engine;

/// <summary>
/// A refinement (crop of an extracted photo) must move the photo's box on the scan, so that saving the
/// session and re-extracting reproduces the refined photo.
/// </summary>
public sealed class RefinementPersistenceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _scanPath;

    // A 160x80 box whose top-left corner is at (420, 460).
    private static readonly RotatedRect Box = new(new Point2f(500, 500), new Size2f(160, 80), 0);

    public RefinementPersistenceTests()
    {
        _tempDir = TestImageFactory.CreateTempDirectory("RefinementPersistenceTests");
        _scanPath = Path.Combine(_tempDir, "standard_scan.jpg");
        TestImageFactory.CreateStandardTwoPhotoScan(_scanPath);
    }

    private static void ShouldBeBox(RotatedRect actual, float cx, float cy, float longSide, float shortSide)
    {
        actual.Center.X.ShouldBe(cx, 0.6f);
        actual.Center.Y.ShouldBe(cy, 0.6f);
        Math.Max(actual.Size.Width, actual.Size.Height).ShouldBe(longSide, 0.6f);
        Math.Min(actual.Size.Width, actual.Size.Height).ShouldBe(shortSide, 0.6f);
    }

    [Fact]
    public void MapPhotoCropToScanUnrotatedShouldOffsetFromBoxCorner()
    {
        var mapped = PhotoCropper.Core.Detection.CropGeometry.MapPhotoCropToScan(Box, 0, new Size(160, 80), new Rect(10, 10, 100, 50));

        ShouldBeBox(mapped, 420 + 10 + 50, 460 + 10 + 25, 100, 50);
    }

    // The shown photo's left half corresponds to a different half of the box depending on rotation.
    [Theory]
    [InlineData(90, 80, 160, 500, 520)]   // left half of a CW-rotated photo = bottom half of the box
    [InlineData(180, 160, 80, 540, 500)]  // left half of an upside-down photo = right half of the box
    [InlineData(270, 80, 160, 500, 480)]  // left half of a CCW-rotated photo = top half of the box
    public void MapPhotoCropToScanRotatedShouldMapLeftHalfToTheMatchingSideOfTheBox(
        int rotation, int shownWidth, int shownHeight, float expectedCx, float expectedCy)
    {
        var leftHalf = new Rect(0, 0, shownWidth / 2, shownHeight);

        var mapped = PhotoCropper.Core.Detection.CropGeometry.MapPhotoCropToScan(Box, rotation, new Size(shownWidth, shownHeight), leftHalf);

        mapped.Center.X.ShouldBe(expectedCx, 0.6f);
        mapped.Center.Y.ShouldBe(expectedCy, 0.6f);
        (mapped.Size.Width * mapped.Size.Height).ShouldBe(160 * 80 / 2f, 1f);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RefinedCropSavedAndRestoredShouldReproduceTheRefinedPhoto(int userQuarterTurns)
    {
        using var engine = new PhotoCropperEngine(_scanPath) { AutoOrientPhotos = false };
        engine.RestoreFromSavedCrops([new WorkspaceCropData { CenterX = 500, CenterY = 500, Width = 160, Height = 80 }]);
        for (int i = 0; i < userQuarterTurns; i++) engine.RotatePhoto(0);

        var shown = engine.DetectedPhotos[0].Size();
        engine.ApplyCropToPhoto(0, new Rect(5, 7, shown.Width - 20, shown.Height - 15));
        var refinedSize = engine.DetectedPhotos[0].Size();

        // Persist the way the GUI does on export, then restore into a fresh engine.
        var cand = engine.AcceptedCandidates[0];
        var saved = new WorkspaceCropData
        {
            CenterX = cand.Rotated.Center.X,
            CenterY = cand.Rotated.Center.Y,
            Width = cand.Rotated.Size.Width,
            Height = cand.Rotated.Size.Height,
            Angle = cand.Rotated.Angle,
            RotationDegrees = engine.PhotoRotations.GetValueOrDefault(0),
            AutoRotationDegrees = cand.OrientationDegrees
        };

        using var restored = new PhotoCropperEngine(_scanPath) { AutoOrientPhotos = false };
        restored.RestoreFromSavedCrops([saved]);

        // Within a pixel: the box is stored in sub-pixel scan coordinates and re-extracted.
        Math.Abs(restored.DetectedPhotos[0].Width - refinedSize.Width).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(restored.DetectedPhotos[0].Height - refinedSize.Height).ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public void ApplyCropToPhotoShouldRedrawTheBoxAtTheRefinedArea()
    {
        using var engine = new PhotoCropperEngine(_scanPath) { AutoOrientPhotos = false };
        engine.RestoreFromSavedCrops([new WorkspaceCropData { CenterX = 500, CenterY = 500, Width = 160, Height = 80 }]);

        engine.ApplyCropToPhoto(0, new Rect(10, 10, 100, 50));

        ShouldBeBox(engine.AcceptedCandidates[0].Rotated, 480, 495, 100, 50);
    }

    [Fact]
    public void RestoreFromSavedCropsWithSavedOrientationShouldReuseItInsteadOfDetecting()
    {
        using var engine = new PhotoCropperEngine(_scanPath) { AutoOrientPhotos = true };

        engine.RestoreFromSavedCrops([new WorkspaceCropData { CenterX = 500, CenterY = 500, Width = 160, Height = 80, AutoRotationDegrees = 90 }]);

        engine.AcceptedCandidates[0].OrientationDegrees.ShouldBe(90);
        engine.DetectedPhotos[0].Width.ShouldBeLessThan(engine.DetectedPhotos[0].Height);
        engine.PhotoRotations.ContainsKey(0).ShouldBeFalse("auto-orientation is not a user rotation");
    }

    [Theory]
    [InlineData(null, 90)] // an old project's photo, its orientation forgotten when it was upgraded: detected again
    [InlineData(0, 0)]     // a saved orientation is reused as it is
    public void RestoreFromSavedCropsWithoutSavedOrientationShouldDetectIt(int? saved, int expected)
    {
        // A landscape photo lying on its side on a white scan.
        string scanPath = Path.Combine(_tempDir, "turned_landscape.png");
        using (var scan = new OpenCvSharp.Mat(600, 600, OpenCvSharp.MatType.CV_8UC3, OpenCvSharp.Scalar.All(245)))
        using (var photo = TestImageFactory.CreateSkyLandscapePhoto(240, 240, orientation: 90))
        using (var target = new OpenCvSharp.Mat(scan, new OpenCvSharp.Rect(180, 180, 240, 240)))
        {
            photo.CopyTo(target);
            OpenCvSharp.Cv2.ImWrite(scanPath, scan);
        }
        using var engine = new PhotoCropperEngine(scanPath) { AutoOrientPhotos = true };

        engine.RestoreFromSavedCrops([new WorkspaceCropData { CenterX = 300, CenterY = 300, Width = 240, Height = 240, AutoRotationDegrees = saved }]);

        engine.AcceptedCandidates[0].OrientationDegrees.ShouldBe(expected);
    }

    [Fact]
    public void AddManualCropShouldKeepPhotosRawCopiesAndBoxesAligned()
    {
        using var engine = new PhotoCropperEngine(_scanPath);
        engine.DetectPhotos();
        int before = engine.DetectedPhotos.Count;

        engine.AddManualCrop(new Rect(1500, 1500, 300, 200));

        engine.DetectedPhotos.Count.ShouldBe(before + 1);
        engine.RawDetectedPhotos.Count.ShouldBe(engine.DetectedPhotos.Count);
        engine.AcceptedCandidates.Count.ShouldBe(engine.DetectedPhotos.Count);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }
        catch (IOException)
        {
        }
    }
}
