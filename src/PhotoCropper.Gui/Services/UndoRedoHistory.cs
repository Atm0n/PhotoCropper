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

internal sealed class BatchAction : IUndoableAction
{
    private readonly int _scanIndex;
    private readonly IReadOnlyList<IUndoableAction> _actions;
    private bool _isDisposed;

    public string Description { get; }
    public int ScanIndex => _scanIndex;

    public BatchAction(int scanIndex, IReadOnlyList<IUndoableAction> actions, string description)
    {
        ArgumentNullException.ThrowIfNull(actions);
        _scanIndex = scanIndex;
        _actions = actions;
        Description = description;
    }

    public void Undo(PhotoCropperEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (_isDisposed) return;

        for (int i = _actions.Count - 1; i >= 0; i--)
        {
            _actions[i].Undo(engine);
        }
    }

    public void Redo(PhotoCropperEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (_isDisposed) return;

        for (int i = 0; i < _actions.Count; i++)
        {
            _actions[i].Redo(engine);
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            foreach (var action in _actions)
            {
                action.Dispose();
            }
            _isDisposed = true;
        }
    }
}

internal sealed class UndoRedoHistory : IDisposable
{
    public const int DefaultMaxCapacity = 30;

    private readonly int _maxCapacity;
    private readonly LinkedList<IUndoableAction> _undoList = new();
    private readonly LinkedList<IUndoableAction> _redoList = new();

    public int MaxCapacity => _maxCapacity;
    public int UndoCount => _undoList.Count;
    public int RedoCount => _redoList.Count;
    public bool CanUndo => _undoList.Count > 0;
    public bool CanRedo => _redoList.Count > 0;

    public UndoRedoHistory(int maxCapacity = DefaultMaxCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCapacity, 1);
        _maxCapacity = maxCapacity;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Action ownership is transferred to _undoList and disposed on Clear/Dispose/Eviction")]
    public void PushDelete(int scanIndex, int index, Mat photoMat)
    {
        PushAction(new DeletePhotoAction(scanIndex, index, photoMat));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Action ownership is transferred to _undoList and disposed on Clear/Dispose/Eviction")]
    public void PushRotate(int scanIndex, int index)
    {
        PushAction(new RotatePhotoAction(scanIndex, index));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Action ownership is transferred to _undoList and disposed on Clear/Dispose/Eviction")]
    public void PushReplace(int scanIndex, int index, Mat previousMat, Mat newMat, string description = "Crop Refinement")
    {
        PushAction(new ReplacePhotoAction(scanIndex, index, previousMat, newMat, description));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Action ownership is transferred to _undoList and disposed on Clear/Dispose/Eviction")]
    public void PushAdd(int scanIndex, int index, Mat addedMat)
    {
        PushAction(new AddPhotoAction(scanIndex, index, addedMat));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Action ownership is transferred to _undoList and disposed on Clear/Dispose/Eviction")]
    public void PushBatch(int scanIndex, IReadOnlyList<IUndoableAction> actions, string description)
    {
        PushAction(new BatchAction(scanIndex, actions, description));
    }

    public void PushAction(IUndoableAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_undoList.Count >= _maxCapacity)
        {
            var oldest = _undoList.First!.Value;
            _undoList.RemoveFirst();
            oldest.Dispose();
        }

        _undoList.AddLast(action);
        ClearRedoStack();
    }

    public IUndoableAction? Undo(IReadOnlyList<PhotoCropperEngine> engines)
    {
        ArgumentNullException.ThrowIfNull(engines);
        return Undo(idx => idx >= 0 && idx < engines.Count ? engines[idx] : null);
    }

    public IUndoableAction? Undo(Func<int, PhotoCropperEngine?> engineAccessor)
    {
        ArgumentNullException.ThrowIfNull(engineAccessor);
        if (_undoList.Count == 0) return null;

        var action = _undoList.Last!.Value;
        _undoList.RemoveLast();

        var engine = engineAccessor(action.ScanIndex);
        if (engine != null)
        {
            action.Undo(engine);
        }

        if (_redoList.Count >= _maxCapacity)
        {
            var oldestRedo = _redoList.First!.Value;
            _redoList.RemoveFirst();
            oldestRedo.Dispose();
        }

        _redoList.AddLast(action);
        return action;
    }

    public IUndoableAction? Redo(IReadOnlyList<PhotoCropperEngine> engines)
    {
        ArgumentNullException.ThrowIfNull(engines);
        return Redo(idx => idx >= 0 && idx < engines.Count ? engines[idx] : null);
    }

    public IUndoableAction? Redo(Func<int, PhotoCropperEngine?> engineAccessor)
    {
        ArgumentNullException.ThrowIfNull(engineAccessor);
        if (_redoList.Count == 0) return null;

        var action = _redoList.Last!.Value;
        _redoList.RemoveLast();

        var engine = engineAccessor(action.ScanIndex);
        if (engine != null)
        {
            action.Redo(engine);
        }

        if (_undoList.Count >= _maxCapacity)
        {
            var oldestUndo = _undoList.First!.Value;
            _undoList.RemoveFirst();
            oldestUndo.Dispose();
        }

        _undoList.AddLast(action);
        return action;
    }

    public void Clear()
    {
        while (_undoList.Count > 0)
        {
            var action = _undoList.Last!.Value;
            _undoList.RemoveLast();
            action.Dispose();
        }
        ClearRedoStack();
    }

    private void ClearRedoStack()
    {
        while (_redoList.Count > 0)
        {
            var action = _redoList.Last!.Value;
            _redoList.RemoveLast();
            action.Dispose();
        }
    }

    public void Dispose()
    {
        Clear();
    }
}