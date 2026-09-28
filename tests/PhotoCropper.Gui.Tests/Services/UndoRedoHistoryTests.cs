using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Core;
using PhotoCropper.Core.Models;
using PhotoCropper.Gui.Services;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Gui.Tests.Services;

public sealed class UndoRedoHistoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _scanPath;

    public UndoRedoHistoryTests()
    {
        _tempDir = TestImageFactory.CreateTempDirectory("UndoRedoTests");
        _scanPath = Path.Combine(_tempDir, "sample_scan.jpg");
        TestImageFactory.CreateStandardTwoPhotoScan(_scanPath);
    }

    [Fact]
    public void UndoRedoHistory_InitialState_ShouldHaveEmptyStacks()
    {
        using var history = new UndoRedoHistory();
        history.CanUndo.ShouldBeFalse();
        history.CanRedo.ShouldBeFalse();
    }

    [Fact]
    public void UndoRedoHistory_PushDelete_ShouldUndoAndRedo()
    {
        using var history = new UndoRedoHistory();
        using var engine = new PhotoCropperEngine(_scanPath);
        engine.DetectPhotos();
        int initialCount = engine.DetectedPhotos.Count;
        int initialCandCount = engine.AcceptedCandidates.Count;
        initialCount.ShouldBeGreaterThanOrEqualTo(2);

        // Delete photo at index 0
        using var photoToDelete = engine.DetectedPhotos[0].Clone();
        var candToDelete = engine.AcceptedCandidates.Count > 0 ? engine.AcceptedCandidates[0] : (CropCandidate?)null;
        engine.DeletePhoto(0);
        history.PushDelete(0, 0, photoToDelete, candToDelete);

        engine.DetectedPhotos.Count.ShouldBe(initialCount - 1);
        engine.AcceptedCandidates.Count.ShouldBe(initialCandCount - 1);
        history.CanUndo.ShouldBeTrue();
        history.CanRedo.ShouldBeFalse();

        // Undo deletion
        var undoAction = history.Undo([engine]);
        undoAction.ShouldNotBeNull();
        undoAction.Description.ShouldBe("Delete Photo");
        engine.DetectedPhotos.Count.ShouldBe(initialCount);
        engine.AcceptedCandidates.Count.ShouldBe(initialCandCount);
        history.CanUndo.ShouldBeFalse();
        history.CanRedo.ShouldBeTrue();

        // Redo deletion
        var redoAction = history.Redo([engine]);
        redoAction.ShouldNotBeNull();
        engine.DetectedPhotos.Count.ShouldBe(initialCount - 1);
        engine.AcceptedCandidates.Count.ShouldBe(initialCandCount - 1);
        history.CanUndo.ShouldBeTrue();
        history.CanRedo.ShouldBeFalse();
    }

    [Fact]
    public void UndoRedoHistory_PushRotate_ShouldUndoAndRedo()
    {
        using var history = new UndoRedoHistory();
        using var engine = new PhotoCropperEngine(_scanPath);
        engine.DetectPhotos();
        engine.DetectedPhotos.ShouldNotBeEmpty();

        int origWidth = engine.DetectedPhotos[0].Width;
        int origHeight = engine.DetectedPhotos[0].Height;

        // Rotate photo 0 clockwise 90 degrees
        engine.RotatePhoto(0);
        history.PushRotate(0, 0);

        engine.DetectedPhotos[0].Width.ShouldBe(origHeight);
        engine.DetectedPhotos[0].Height.ShouldBe(origWidth);

        // Undo rotation
        var undoAction = history.Undo([engine]);
        undoAction.ShouldNotBeNull();
        undoAction.Description.ShouldBe("Rotate Photo");
        engine.DetectedPhotos[0].Width.ShouldBe(origWidth);
        engine.DetectedPhotos[0].Height.ShouldBe(origHeight);

        // Redo rotation
        var redoAction = history.Redo([engine]);
        redoAction.ShouldNotBeNull();
        engine.DetectedPhotos[0].Width.ShouldBe(origHeight);
        engine.DetectedPhotos[0].Height.ShouldBe(origWidth);
    }

    [Fact]
    public void UndoRedoHistory_PushAdd_ShouldUndoAndRedo()
    {
        using var history = new UndoRedoHistory();
        using var engine = new PhotoCropperEngine(_scanPath);
        engine.DetectPhotos();
        int initialCount = engine.DetectedPhotos.Count;

        using Mat manualCrop = new(150, 150, DepthType.Cv8U, 3);
        manualCrop.SetTo(new MCvScalar(100, 100, 100));

        engine.DetectedPhotos.Add(manualCrop.Clone());
        history.PushAdd(0, engine.DetectedPhotos.Count - 1, manualCrop);

        engine.DetectedPhotos.Count.ShouldBe(initialCount + 1);

        // Undo addition
        var undoAction = history.Undo([engine]);
        undoAction.ShouldNotBeNull();
        undoAction.Description.ShouldBe("Add Manual Crop");
        engine.DetectedPhotos.Count.ShouldBe(initialCount);

        // Redo addition
        var redoAction = history.Redo([engine]);
        redoAction.ShouldNotBeNull();
        engine.DetectedPhotos.Count.ShouldBe(initialCount + 1);
    }

    [Fact]
    public void UndoRedoHistory_PushReplace_ShouldUndoAndRedo()
    {
        using var history = new UndoRedoHistory();
        using var engine = new PhotoCropperEngine(_scanPath);
        engine.DetectPhotos();
        engine.DetectedPhotos.ShouldNotBeEmpty();

        using var prevMat = engine.DetectedPhotos[0].Clone();
        using var newMat = new Mat(50, 50, DepthType.Cv8U, 3);
        newMat.SetTo(new MCvScalar(200, 200, 200));

        engine.DetectedPhotos[0].Dispose();
        engine.DetectedPhotos[0] = newMat.Clone();
        history.PushReplace(0, 0, prevMat, newMat, "Refine Crop");

        engine.DetectedPhotos[0].Width.ShouldBe(50);

        // Undo replacement
        var undoAction = history.Undo([engine]);
        undoAction.ShouldNotBeNull();
        undoAction.Description.ShouldBe("Refine Crop");
        engine.DetectedPhotos[0].Width.ShouldBe(prevMat.Width);

        // Redo replacement
        var redoAction = history.Redo([engine]);
        redoAction.ShouldNotBeNull();
        engine.DetectedPhotos[0].Width.ShouldBe(50);
    }

    [Fact]
    public void UndoRedoHistory_Clear_ShouldResetAllStacks()
    {
        using var history = new UndoRedoHistory();
        using Mat dummy = new(10, 10, DepthType.Cv8U, 3);
        history.PushAdd(0, 0, dummy);
        history.CanUndo.ShouldBeTrue();

        history.Clear();
        history.CanUndo.ShouldBeFalse();
        history.CanRedo.ShouldBeFalse();
    }

    [Fact]
    public void UndoRedoHistory_PushBatch_ShouldUndoAndRedoBatchActions()
    {
        using var history = new UndoRedoHistory();
        using var engine = new PhotoCropperEngine(_scanPath);
        engine.DetectPhotos();
        engine.DetectedPhotos.Count.ShouldBeGreaterThanOrEqualTo(2);

        int origWidth0 = engine.DetectedPhotos[0].Width;
        int origHeight0 = engine.DetectedPhotos[0].Height;
        int origWidth1 = engine.DetectedPhotos[1].Width;
        int origHeight1 = engine.DetectedPhotos[1].Height;

        // Rotate both photo 0 and photo 1
        engine.RotatePhoto(0);
        engine.RotatePhoto(1);

        var actions = new List<IUndoableAction>
        {
            new RotatePhotoAction(0, 0),
            new RotatePhotoAction(0, 1)
        };
        history.PushBatch(0, actions, "Rotate 2 Photos");

        engine.DetectedPhotos[0].Width.ShouldBe(origHeight0);
        engine.DetectedPhotos[1].Width.ShouldBe(origHeight1);

        // Undo batch rotation
        var undoAction = history.Undo([engine]);
        undoAction.ShouldNotBeNull();
        undoAction.Description.ShouldBe("Rotate 2 Photos");
        engine.DetectedPhotos[0].Width.ShouldBe(origWidth0);
        engine.DetectedPhotos[1].Width.ShouldBe(origWidth1);

        // Redo batch rotation
        var redoAction = history.Redo([engine]);
        redoAction.ShouldNotBeNull();
        engine.DetectedPhotos[0].Width.ShouldBe(origHeight0);
        engine.DetectedPhotos[1].Width.ShouldBe(origHeight1);
    }

    [Fact]
    public void UndoRedoHistory_PushBatch_BatchDelete_ShouldUndoAndRedoInDescendingOrder()
    {
        using var history = new UndoRedoHistory();
        using var engine = new PhotoCropperEngine(_scanPath);
        engine.DetectPhotos();
        engine.DetectedPhotos.Count.ShouldBeGreaterThanOrEqualTo(2);
        int initialCount = engine.DetectedPhotos.Count;
        int initialCandCount = engine.AcceptedCandidates.Count;

        int origWidth0 = engine.DetectedPhotos[0].Width;
        int origWidth1 = engine.DetectedPhotos[1].Width;

        var mat1 = engine.DetectedPhotos[1];
        var mat0 = engine.DetectedPhotos[0];
        var cand1 = engine.AcceptedCandidates.Count > 1 ? engine.AcceptedCandidates[1] : (CropCandidate?)null;
        var cand0 = engine.AcceptedCandidates.Count > 0 ? engine.AcceptedCandidates[0] : (CropCandidate?)null;

        var actions = new List<IUndoableAction>
        {
            new DeletePhotoAction(0, 1, mat1, cand1),
            new DeletePhotoAction(0, 0, mat0, cand0)
        };

        // Execute deletes in descending order
        engine.DeletePhoto(1);
        engine.DeletePhoto(0);
        engine.DetectedPhotos.Count.ShouldBe(initialCount - 2);
        engine.AcceptedCandidates.Count.ShouldBe(initialCandCount - 2);

        history.PushBatch(0, actions, "Delete 2 Photos");

        // Undo batch delete (should restore in reverse order: 0 then 1)
        var undoAction = history.Undo([engine]);
        undoAction.ShouldNotBeNull();
        undoAction.Description.ShouldBe("Delete 2 Photos");
        engine.DetectedPhotos.Count.ShouldBe(initialCount);
        engine.AcceptedCandidates.Count.ShouldBe(initialCandCount);
        engine.DetectedPhotos[0].Width.ShouldBe(origWidth0);
        engine.DetectedPhotos[1].Width.ShouldBe(origWidth1);

        // Redo batch delete (should delete 1 then 0)
        var redoAction = history.Redo([engine]);
        redoAction.ShouldNotBeNull();
        engine.DetectedPhotos.Count.ShouldBe(initialCount - 2);
        engine.AcceptedCandidates.Count.ShouldBe(initialCandCount - 2);
    }

    [Fact]
    public void UndoRedoHistory_BoundedCapacity_ShouldEvictAndDisposeOldestActions()
    {
        const int capacity = 3;
        using var history = new UndoRedoHistory(capacity);
        using var engine = new PhotoCropperEngine(_scanPath);

        // Push 5 actions (exceeding capacity of 3)
        history.PushRotate(0, 0);
        history.PushRotate(0, 1);
        history.PushRotate(0, 2);
        history.PushRotate(0, 3);
        history.PushRotate(0, 4);

        history.UndoCount.ShouldBe(capacity);

        // The top of undo stack should be action 4, then 3, then 2 (0 and 1 evicted)
        var a1 = history.Undo([engine]);
        a1.ShouldNotBeNull();

        var a2 = history.Undo([engine]);
        a2.ShouldNotBeNull();

        var a3 = history.Undo([engine]);
        a3.ShouldNotBeNull();

        // No more undo actions available
        history.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public void UndoRedoHistory_InvalidateScan_ShouldRemoveUndoEntriesForThatScan()
    {
        using var history = new UndoRedoHistory();

        // Push actions for scan 0 and scan 1
        history.PushRotate(0, 0);
        history.PushRotate(1, 0);
        history.PushRotate(0, 1);
        history.UndoCount.ShouldBe(3);

        // Invalidate scan 1 — only its action should be removed
        history.InvalidateScan(1);

        history.UndoCount.ShouldBe(2);
        history.CanUndo.ShouldBeTrue();
    }

    [Fact]
    public void UndoRedoHistory_InvalidateScan_ShouldAlsoClearRedoEntriesForThatScan()
    {
        using var history = new UndoRedoHistory();
        using var engine = new PhotoCropperEngine(_scanPath);
        engine.DetectPhotos();

        // Undo pops the LAST pushed entry. Push scan 0 first, then scan 1 last.
        // Undo() will pop the scan 1 action (last in) and place it on the redo stack.
        history.PushRotate(0, 0);  // scan 0 action pushed first — stays on undo stack
        history.PushRotate(1, 0);  // scan 1 action pushed last  — will be moved to redo
        history.Undo([engine]);    // pops scan 1 rotate → redo stack; scan 0 remains on undo

        history.CanRedo.ShouldBeTrue();
        history.UndoCount.ShouldBe(1); // scan 0 action remains on undo

        // Invalidate scan 1 — its redo entry should be purged
        history.InvalidateScan(1);

        history.CanRedo.ShouldBeFalse();
        history.UndoCount.ShouldBe(1); // scan 0 undo entry untouched
    }

    [Fact]
    public void UndoRedoHistory_InvalidateScan_WhenNoEntriesForScan_ShouldLeaveOtherEntriesIntact()
    {
        using var history = new UndoRedoHistory();
        history.PushRotate(0, 0);
        history.PushRotate(0, 1);
        history.UndoCount.ShouldBe(2);

        // Invalidating a scan that has no entries should be a no-op
        history.InvalidateScan(99);

        history.UndoCount.ShouldBe(2);
        history.CanUndo.ShouldBeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
