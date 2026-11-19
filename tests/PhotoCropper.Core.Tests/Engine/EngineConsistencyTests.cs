using OpenCvSharp;
using PhotoCropper.Core.Workspace;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Core.Tests.Engine;

/// <summary>
/// Regression tests keeping DetectedPhotos, RawDetectedPhotos, AcceptedCandidates and PhotoRotations in step.
/// </summary>
public sealed class EngineConsistencyTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _scanPath;

    public EngineConsistencyTests()
    {
        _tempDir = TestImageFactory.CreateTempDirectory("EngineConsistencyTests");
        _scanPath = Path.Combine(_tempDir, "standard_scan.jpg");
        TestImageFactory.CreateStandardTwoPhotoScan(_scanPath);
    }

    private PhotoCropperEngine CreateEngineWithCrops(params WorkspaceCropData[] crops)
    {
        var engine = new PhotoCropperEngine(_scanPath) { AutoOrientPhotos = false };
        engine.RestoreFromSavedCrops(crops);
        return engine;
    }

    private static WorkspaceCropData Crop(float cx, float cy, float w, float h, int rotation = 0) =>
        new() { CenterX = cx, CenterY = cy, Width = w, Height = h, Angle = 0, RotationDegrees = rotation };

    private static bool AreIdentical(Mat a, Mat b) =>
        a.Size() == b.Size() && a.Channels() == b.Channels() && Cv2.Norm(a, b, NormTypes.L1) == 0;

    [Fact]
    public void DetectPhotos_AfterRotation_ShouldClearStaleRotations()
    {
        using var engine = new PhotoCropperEngine(_scanPath);
        engine.DetectPhotos();
        engine.RotatePhoto(0);
        engine.PhotoRotations.ShouldContainKey(0);

        engine.DetectPhotos();

        engine.PhotoRotations.ShouldBeEmpty();
    }

    [Fact]
    public void RestoreFromSavedCrops_WhenACropCannotBeExtracted_ShouldKeepPhotosCandidatesAndRotationsAligned()
    {
        // The first crop is too small to extract and is dropped; the others must keep their own box and rotation.
        using var engine = CreateEngineWithCrops(
            Crop(300, 300, 5, 5),
            Crop(500, 500, 160, 80, rotation: 90),
            Crop(900, 900, 120, 60));

        engine.DetectedPhotos.Count.ShouldBe(2);
        engine.RawDetectedPhotos.Count.ShouldBe(2);
        engine.AcceptedCandidates.Count.ShouldBe(2);

        engine.AcceptedCandidates[0].Rotated.Size.Width.ShouldBe(160);
        engine.AcceptedCandidates[1].Rotated.Size.Width.ShouldBe(120);

        // Photo 0 (the 160x80 crop) carries the saved 90° rotation; photo 1 is untouched.
        engine.PhotoRotations[0].ShouldBe(90);
        engine.PhotoRotations.ContainsKey(1).ShouldBeFalse();
        engine.DetectedPhotos[0].Width.ShouldBeLessThan(engine.DetectedPhotos[0].Height);
        engine.DetectedPhotos[1].Width.ShouldBeGreaterThan(engine.DetectedPhotos[1].Height);
    }

    [Theory]
    [InlineData("standard")]
    [InlineData("flush")]
    [InlineData("corner")]
    public void RestoreFromSavedCrops_OfDetectedBoxes_ShouldGiveTheSamePixels(string kind)
    {
        // Reopening a project restores the saved boxes; the photos must come out exactly as detected, or the
        // project's cropped files are rewritten and their final photos rendered again for nothing.
        string path = Path.Combine(_tempDir, $"{kind}.png");
        switch (kind)
        {
            case "flush": TestImageFactory.CreateFlushEdgeScan(path); break;
            case "corner": TestImageFactory.CreateCornerPhotoScan(path); break;
            default: TestImageFactory.CreateStandardTwoPhotoScan(path); break;
        }

        using var detected = new PhotoCropperEngine(path);
        detected.DetectPhotos();
        detected.RawDetectedPhotos.ShouldNotBeEmpty();

        using var restored = new PhotoCropperEngine(path);
        restored.RestoreFromSavedCrops(WorkspaceCropData.FromEngine(detected));

        restored.RawDetectedPhotos.Count.ShouldBe(detected.RawDetectedPhotos.Count);
        for (int i = 0; i < detected.RawDetectedPhotos.Count; i++)
        {
            AreIdentical(restored.RawDetectedPhotos[i], detected.RawDetectedPhotos[i]).ShouldBeTrue($"photo {i}");
        }
    }

    [Fact]
    public void RestoreFromSavedCrops_WithNegativeRotation_ShouldNormalizeToClockwiseSteps()
    {
        using var engine = CreateEngineWithCrops(Crop(500, 500, 160, 80, rotation: -90));

        engine.PhotoRotations[0].ShouldBe(270);
    }

    [Fact]
    public void ApplyGrabHandleResize_ShouldKeepUserRotation()
    {
        using var engine = CreateEngineWithCrops(Crop(500, 500, 160, 80));
        engine.RotatePhoto(0);
        var rotatedSize = engine.DetectedPhotos[0].Size();

        engine.ApplyGrabHandleResize(0);

        engine.PhotoRotations[0].ShouldBe(90);
        engine.DetectedPhotos[0].Size().ShouldBe(rotatedSize);
        engine.RawDetectedPhotos[0].Size().ShouldBe(rotatedSize);
    }

    [Fact]
    public void ApplyGrabHandleResize_ShouldKeepRawCopyUnrestored()
    {
        using var engine = new PhotoCropperEngine(_scanPath) { AutoOrientPhotos = false, RestoreVintageColors = true };
        engine.RestoreFromSavedCrops([Crop(500, 500, 160, 80)]);
        using var rawBefore = engine.RawDetectedPhotos[0].Clone();

        // Re-extracting the same box must reproduce the same raw (pre-restoration) copy.
        engine.ApplyGrabHandleResize(0);

        AreIdentical(engine.RawDetectedPhotos[0], rawBefore).ShouldBeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void ApplyGrabHandleResize_OutOfRange_ShouldDoNothing(int index)
    {
        using var engine = CreateEngineWithCrops(Crop(500, 500, 160, 80));

        Should.NotThrow(() => engine.ApplyGrabHandleResize(index));
        engine.DetectedPhotos.Count.ShouldBe(1);
    }

    [Fact]
    public void ApplyGrabHandleResize_WhenBoxIsTooSmall_ShouldReportFailureAndKeepThePhoto()
    {
        using var engine = CreateEngineWithCrops(Crop(500, 500, 160, 80));
        var sizeBefore = engine.DetectedPhotos[0].Size();
        var tiny = engine.AcceptedCandidates[0] with
        {
            Rotated = new PhotoCropper.Core.Models.RotatedBox(new System.Drawing.PointF(500, 500), new System.Drawing.SizeF(6, 6), 0)
        };
        engine.UpdateCandidates([tiny]);

        engine.ApplyGrabHandleResize(0).ShouldBeFalse();
        engine.DetectedPhotos[0].Size().ShouldBe(sizeBefore);
    }

    [Fact]
    public void ApplyGrabHandleResize_WhenExtracted_ShouldReportSuccess()
    {
        using var engine = CreateEngineWithCrops(Crop(500, 500, 160, 80));

        engine.ApplyGrabHandleResize(0).ShouldBeTrue();
    }

    [Fact]
    public void InsertPhoto_ShouldRestoreRotationAndRawCopy()
    {
        using var engine = CreateEngineWithCrops(Crop(500, 500, 160, 80), Crop(900, 900, 120, 60));
        using var photo = engine.DetectedPhotos[1].Clone();
        using var raw = new Mat(photo.Size(), photo.Type());
        raw.SetTo(new Scalar(1, 2, 3, 255));

        engine.InsertPhoto(0, photo.Clone(), engine.AcceptedCandidates[1], raw, rotation: 90);

        engine.PhotoRotations[0].ShouldBe(90);
        AreIdentical(engine.RawDetectedPhotos[0], raw).ShouldBeTrue();
    }

    [Fact]
    public void ApplyCropToPhoto_ShouldCropRawCopyToo()
    {
        using var engine = CreateEngineWithCrops(Crop(500, 500, 160, 80));

        engine.ApplyCropToPhoto(0, new System.Drawing.Rectangle(10, 10, 100, 50));

        engine.DetectedPhotos[0].Size().ShouldBe(new Size(100, 50));
        engine.RawDetectedPhotos[0].Size().ShouldBe(new Size(100, 50));
    }

    [Fact]
    public void DeletePhoto_ShouldNotMutatePreviouslyReturnedCandidateList()
    {
        using var engine = new PhotoCropperEngine(_scanPath);
        engine.DetectPhotos();
        var snapshot = engine.AcceptedCandidates;
        int countBefore = snapshot.Count;

        engine.DeletePhoto(0);

        snapshot.Count.ShouldBe(countBefore);
        engine.AcceptedCandidates.Count.ShouldBe(countBefore - 1);
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
