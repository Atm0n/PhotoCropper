using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper;
using PhotoCropper.Tests.Helpers;
using PhotoCropperGui.Services;

namespace PhotoCropper.Tests.Gui;

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
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void UndoRedoHistory_PushDelete_ShouldUndoAndRedo()
    {
        using var history = new UndoRedoHistory();
        using var engine = new PhotoCropperEngine(_scanPath);
        engine.DetectPhotos();
        int initialCount = engine.DetectedPhotos.Count;
        Assert.True(initialCount >= 2);

        // Delete photo at index 0
        using var photoToDelete = engine.DetectedPhotos[0].Clone();
        engine.DeletePhoto(0);
        history.PushDelete(0, 0, photoToDelete);

        Assert.Equal(initialCount - 1, engine.DetectedPhotos.Count);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);

        // Undo deletion
        var undoAction = history.Undo([engine]);
        Assert.NotNull(undoAction);
        Assert.Equal("Delete Photo", undoAction.Description);
        Assert.Equal(initialCount, engine.DetectedPhotos.Count);
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);

        // Redo deletion
        var redoAction = history.Redo([engine]);
        Assert.NotNull(redoAction);
        Assert.Equal(initialCount - 1, engine.DetectedPhotos.Count);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void UndoRedoHistory_PushRotate_ShouldUndoAndRedo()
    {
        using var history = new UndoRedoHistory();
        using var engine = new PhotoCropperEngine(_scanPath);
        engine.DetectPhotos();
        Assert.NotEmpty(engine.DetectedPhotos);

        int origWidth = engine.DetectedPhotos[0].Width;
        int origHeight = engine.DetectedPhotos[0].Height;

        // Rotate photo 0 clockwise 90 degrees
        engine.RotatePhoto(0);
        history.PushRotate(0, 0);

        Assert.Equal(origHeight, engine.DetectedPhotos[0].Width);
        Assert.Equal(origWidth, engine.DetectedPhotos[0].Height);

        // Undo rotation
        var undoAction = history.Undo([engine]);
        Assert.NotNull(undoAction);
        Assert.Equal("Rotate Photo", undoAction.Description);
        Assert.Equal(origWidth, engine.DetectedPhotos[0].Width);
        Assert.Equal(origHeight, engine.DetectedPhotos[0].Height);

        // Redo rotation
        var redoAction = history.Redo([engine]);
        Assert.NotNull(redoAction);
        Assert.Equal(origHeight, engine.DetectedPhotos[0].Width);
        Assert.Equal(origWidth, engine.DetectedPhotos[0].Height);
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

        Assert.Equal(initialCount + 1, engine.DetectedPhotos.Count);

        // Undo addition
        var undoAction = history.Undo([engine]);
        Assert.NotNull(undoAction);
        Assert.Equal("Add Manual Crop", undoAction.Description);
        Assert.Equal(initialCount, engine.DetectedPhotos.Count);

        // Redo addition
        var redoAction = history.Redo([engine]);
        Assert.NotNull(redoAction);
        Assert.Equal(initialCount + 1, engine.DetectedPhotos.Count);
    }

    [Fact]
    public void UndoRedoHistory_PushReplace_ShouldUndoAndRedo()
    {
        using var history = new UndoRedoHistory();
        using var engine = new PhotoCropperEngine(_scanPath);
        engine.DetectPhotos();
        Assert.NotEmpty(engine.DetectedPhotos);

        using var prevMat = engine.DetectedPhotos[0].Clone();
        using var newMat = new Mat(50, 50, DepthType.Cv8U, 3);
        newMat.SetTo(new MCvScalar(200, 200, 200));

        engine.DetectedPhotos[0].Dispose();
        engine.DetectedPhotos[0] = newMat.Clone();
        history.PushReplace(0, 0, prevMat, newMat, "Refine Crop");

        Assert.Equal(50, engine.DetectedPhotos[0].Width);

        // Undo replacement
        var undoAction = history.Undo([engine]);
        Assert.NotNull(undoAction);
        Assert.Equal("Refine Crop", undoAction.Description);
        Assert.Equal(prevMat.Width, engine.DetectedPhotos[0].Width);

        // Redo replacement
        var redoAction = history.Redo([engine]);
        Assert.NotNull(redoAction);
        Assert.Equal(50, engine.DetectedPhotos[0].Width);
    }

    [Fact]
    public void UndoRedoHistory_Clear_ShouldResetAllStacks()
    {
        using var history = new UndoRedoHistory();
        using Mat dummy = new(10, 10, DepthType.Cv8U, 3);
        history.PushAdd(0, 0, dummy);
        Assert.True(history.CanUndo);

        history.Clear();
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
