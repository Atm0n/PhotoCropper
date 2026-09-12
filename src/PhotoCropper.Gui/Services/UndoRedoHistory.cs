using Emgu.CV;
using Emgu.CV.CvEnum;
using PhotoCropper.Core;

namespace PhotoCropper.Gui.Services;

internal interface IUndoableAction : IDisposable
{
    string Description { get; }
    int ScanIndex { get; }
    void Undo(PhotoCropperEngine engine);
    void Redo(PhotoCropperEngine engine);
}

internal sealed class DeletePhotoAction : IUndoableAction
{
    private readonly int _scanIndex;
    private readonly int _index;
    private Mat _deletedMat;
    private bool _isDisposed;

    public string Description => "Delete Photo";
    public int ScanIndex => _scanIndex;

    public DeletePhotoAction(int scanIndex, int index, Mat photoMat)
    {
        ArgumentNullException.ThrowIfNull(photoMat);
        _scanIndex = scanIndex;
        _index = index;
        _deletedMat = photoMat.Clone();
    }

    public void Undo(PhotoCropperEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (_isDisposed) return;

        int targetIndex = Math.Clamp(_index, 0, engine.DetectedPhotos.Count);
        engine.DetectedPhotos.Insert(targetIndex, _deletedMat.Clone());
    }

    public void Redo(PhotoCropperEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (_index >= 0 && _index < engine.DetectedPhotos.Count)
        {
            engine.DeletePhoto(_index);
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _deletedMat.Dispose();
            _isDisposed = true;
        }
    }
}

internal sealed class RotatePhotoAction : IUndoableAction
{
    private readonly int _scanIndex;
    private readonly int _index;

    public string Description => "Rotate Photo";
    public int ScanIndex => _scanIndex;

    public RotatePhotoAction(int scanIndex, int index)
    {
        _scanIndex = scanIndex;
        _index = index;
    }

    public void Undo(PhotoCropperEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (_index < 0 || _index >= engine.DetectedPhotos.Count) return;

        // Counter-clockwise 90° rotation to undo clockwise 90° rotation
        Mat rotated = new();
        CvInvoke.Rotate(engine.DetectedPhotos[_index], rotated, RotateFlags.Rotate90CounterClockwise);
        engine.DetectedPhotos[_index].Dispose();
        engine.DetectedPhotos[_index] = rotated;
    }

    public void Redo(PhotoCropperEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (_index < 0 || _index >= engine.DetectedPhotos.Count) return;

        engine.RotatePhoto(_index);
    }

    public void Dispose()
    {
        // No unmanaged resources held
    }
}

internal sealed class ReplacePhotoAction : IUndoableAction
{
    private readonly int _scanIndex;
    private readonly int _index;
    private Mat _previousMat;
    private Mat _newMat;
    private bool _isDisposed;

    public string Description { get; }
    public int ScanIndex => _scanIndex;

    public ReplacePhotoAction(int scanIndex, int index, Mat previousMat, Mat newMat, string description = "Crop Refinement")
    {
        ArgumentNullException.ThrowIfNull(previousMat);
        ArgumentNullException.ThrowIfNull(newMat);
        _scanIndex = scanIndex;
        _index = index;
        _previousMat = previousMat.Clone();
        _newMat = newMat.Clone();
        Description = description;
    }

    public void Undo(PhotoCropperEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (_isDisposed || _index < 0 || _index >= engine.DetectedPhotos.Count) return;

        engine.DetectedPhotos[_index].Dispose();
        engine.DetectedPhotos[_index] = _previousMat.Clone();
    }

    public void Redo(PhotoCropperEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (_isDisposed || _index < 0 || _index >= engine.DetectedPhotos.Count) return;

        engine.DetectedPhotos[_index].Dispose();
        engine.DetectedPhotos[_index] = _newMat.Clone();
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _previousMat.Dispose();
            _newMat.Dispose();
            _isDisposed = true;
        }
    }
}

internal sealed class AddPhotoAction : IUndoableAction
{
    private readonly int _scanIndex;
    private readonly int _index;
    private Mat _addedMat;
    private bool _isDisposed;

    public string Description => "Add Manual Crop";
    public int ScanIndex => _scanIndex;

    public AddPhotoAction(int scanIndex, int index, Mat addedMat)
    {
        ArgumentNullException.ThrowIfNull(addedMat);
        _scanIndex = scanIndex;
        _index = index;
        _addedMat = addedMat.Clone();
    }

    public void Undo(PhotoCropperEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (_isDisposed) return;

        if (_index >= 0 && _index < engine.DetectedPhotos.Count)
        {
            engine.DeletePhoto(_index);
        }
    }

    public void Redo(PhotoCropperEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (_isDisposed) return;

        int targetIndex = Math.Clamp(_index, 0, engine.DetectedPhotos.Count);
        engine.DetectedPhotos.Insert(targetIndex, _addedMat.Clone());
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _addedMat.Dispose();
            _isDisposed = true;
        }
    }
}

internal sealed class UndoRedoHistory : IDisposable
{
    private readonly Stack<IUndoableAction> _undoStack = new();
    private readonly Stack<IUndoableAction> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Action ownership is transferred to _undoStack and disposed on Clear/Dispose")]
    public void PushDelete(int scanIndex, int index, Mat photoMat)
    {
        _undoStack.Push(new DeletePhotoAction(scanIndex, index, photoMat));
        ClearRedoStack();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Action ownership is transferred to _undoStack and disposed on Clear/Dispose")]
    public void PushRotate(int scanIndex, int index)
    {
        _undoStack.Push(new RotatePhotoAction(scanIndex, index));
        ClearRedoStack();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Action ownership is transferred to _undoStack and disposed on Clear/Dispose")]
    public void PushReplace(int scanIndex, int index, Mat previousMat, Mat newMat, string description = "Crop Refinement")
    {
        _undoStack.Push(new ReplacePhotoAction(scanIndex, index, previousMat, newMat, description));
        ClearRedoStack();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Action ownership is transferred to _undoStack and disposed on Clear/Dispose")]
    public void PushAdd(int scanIndex, int index, Mat addedMat)
    {
        _undoStack.Push(new AddPhotoAction(scanIndex, index, addedMat));
        ClearRedoStack();
    }

    public void PushAction(IUndoableAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        _undoStack.Push(action);
        ClearRedoStack();
    }

    public IUndoableAction? Undo(IReadOnlyList<PhotoCropperEngine> engines)
    {
        ArgumentNullException.ThrowIfNull(engines);
        if (_undoStack.Count == 0) return null;

        var action = _undoStack.Pop();
        if (action.ScanIndex >= 0 && action.ScanIndex < engines.Count)
        {
            action.Undo(engines[action.ScanIndex]);
        }
        _redoStack.Push(action);
        return action;
    }

    public IUndoableAction? Redo(IReadOnlyList<PhotoCropperEngine> engines)
    {
        ArgumentNullException.ThrowIfNull(engines);
        if (_redoStack.Count == 0) return null;

        var action = _redoStack.Pop();
        if (action.ScanIndex >= 0 && action.ScanIndex < engines.Count)
        {
            action.Redo(engines[action.ScanIndex]);
        }
        _undoStack.Push(action);
        return action;
    }

    public void Clear()
    {
        while (_undoStack.Count > 0)
        {
            _undoStack.Pop().Dispose();
        }
        ClearRedoStack();
    }

    private void ClearRedoStack()
    {
        while (_redoStack.Count > 0)
        {
            _redoStack.Pop().Dispose();
        }
    }

    public void Dispose()
    {
        Clear();
    }
}