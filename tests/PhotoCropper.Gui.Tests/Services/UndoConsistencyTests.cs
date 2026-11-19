using OpenCvSharp;
using PhotoCropper.Core;
using PhotoCropper.Core.Models;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Gui.Tests.Services;

/// <summary>
/// Regression tests: undo/redo must restore the raw compare copy and rotation state, and must not
/// be affected by later changes to lists it was given.
/// </summary>
public sealed class UndoConsistencyTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _scanPath;

    public UndoConsistencyTests()
    {
        _tempDir = TestImageFactory.CreateTempDirectory("UndoConsistencyTests");
        _scanPath = Path.Combine(_tempDir, "sample_scan.jpg");
        TestImageFactory.CreateStandardTwoPhotoScan(_scanPath);
    }

    private PhotoCropperEngine CreateEngineWithOneLandscapeCrop()
    {
        var engine = new PhotoCropperEngine(_scanPath) { AutoOrientPhotos = false };
        engine.RestoreFromSavedCrops([new WorkspaceCropData { CenterX = 500, CenterY = 500, Width = 160, Height = 80 }]);
        return engine;
    }

    [Fact]
    public void UndoCounterClockwiseRotation_ShouldRestoreOrientationRawCopyAndRotationState()
    {
        using var history = new UndoRedoHistory();
        using var engine = CreateEngineWithOneLandscapeCrop();
        var originalSize = engine.DetectedPhotos[0].Size();

        engine.RotatePhotoCounterClockwise(0);
        history.PushRotate(0, 0, clockwise: false);

        history.Undo([engine]);

        engine.DetectedPhotos[0].Size().ShouldBe(originalSize);
        engine.RawDetectedPhotos[0].Size().ShouldBe(originalSize);
        engine.PhotoRotations[0].ShouldBe(0);

        history.Redo([engine]);

        engine.PhotoRotations[0].ShouldBe(270);
    }

    [Fact]
    public void UndoClockwiseRotation_ShouldResetPersistedRotation()
    {
        using var history = new UndoRedoHistory();
        using var engine = CreateEngineWithOneLandscapeCrop();

        engine.RotatePhoto(0);
        history.PushRotate(0, 0);
        history.Undo([engine]);

        engine.PhotoRotations[0].ShouldBe(0);
        engine.RawDetectedPhotos[0].Size().ShouldBe(engine.DetectedPhotos[0].Size());
    }

    [Fact]
    public void ResizeCandidateAction_ShouldNotChangeWhenCallerListIsMutatedLater()
    {
        using var history = new UndoRedoHistory();
        using var engine = new PhotoCropperEngine(_scanPath);
        engine.DetectPhotos();
        int candidateCount = engine.AcceptedCandidates.Count;
        candidateCount.ShouldBeGreaterThanOrEqualTo(2);

        var previous = new List<CropCandidate>(engine.AcceptedCandidates);
        var next = new List<CropCandidate>(engine.AcceptedCandidates);
        history.PushResizeCandidate(0, 0, engine.DetectedPhotos[0], engine.DetectedPhotos[0], previous, next);

        // Simulate the caller's list changing after the action was recorded.
        previous.Clear();
        next.Clear();

        history.Undo([engine]);
        engine.AcceptedCandidates.Count.ShouldBe(candidateCount);

        history.Redo([engine]);
        engine.AcceptedCandidates.Count.ShouldBe(candidateCount);
    }

    [Fact]
    public void UndoDeleteOfRotatedPhoto_ShouldRestoreItsRotationAndRawCopy()
    {
        using var history = new UndoRedoHistory();
        using var engine = CreateEngineWithOneLandscapeCrop();
        engine.RotatePhoto(0);
        using var rawBefore = engine.RawDetectedPhotos[0].Clone();

        history.PushDelete(0, 0, engine);
        engine.DeletePhoto(0);
        history.Undo([engine]);

        engine.PhotoRotations[0].ShouldBe(90);
        engine.RawDetectedPhotos[0].Size().ShouldBe(rawBefore.Size());
        Cv2.Norm(engine.RawDetectedPhotos[0], rawBefore, NormTypes.L1).ShouldBe(0);
    }

    [Fact]
    public void UndoRefinement_ShouldRestoreRawCopy()
    {
        using var history = new UndoRedoHistory();
        using var engine = CreateEngineWithOneLandscapeCrop();
        using var beforeMat = engine.DetectedPhotos[0].Clone();
        using var beforeRaw = engine.RawDetectedPhotos[0].Clone();

        engine.ApplyCropToPhoto(0, new System.Drawing.Rectangle(10, 10, 100, 50));
        history.PushReplace(0, 0, beforeMat, engine.DetectedPhotos[0],
            previousRaw: beforeRaw, newRaw: engine.RawDetectedPhotos[0]);

        history.Undo([engine]);
        engine.RawDetectedPhotos[0].Size().ShouldBe(beforeRaw.Size());

        history.Redo([engine]);
        engine.RawDetectedPhotos[0].Size().ShouldBe(new OpenCvSharp.Size(100, 50));
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
