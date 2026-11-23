using OpenCvSharp;
using PhotoCropper.Core;
using PhotoCropper.Core.Models;

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
    private readonly Mat _deletedMat;
    private readonly Mat? _deletedRaw;
    private readonly CropCandidate? _candidate;
    private readonly int _rotation;
    private bool _isDisposed;

    public string Description => "Delete Photo";
    public int ScanIndex => _scanIndex;

    /// <param name="rawMat">The photo's raw compare copy, restored on undo (defaults to the photo itself).</param>
    /// <param name="rotation">The photo's manual rotation, restored on undo.</param>
    public DeletePhotoAction(int scanIndex, int index, Mat photoMat, CropCandidate? candidate = null, Mat? rawMat = null, int rotation = 0)
    {
        ArgumentNullException.ThrowIfNull(photoMat);
        _scanIndex = scanIndex;
        _index = index;
        _deletedMat = photoMat.Clone();
        _deletedRaw = rawMat?.Clone();
        _candidate = candidate;
        _rotation = rotation;
    }

    /// <summary>Records the full state of photo <paramref name="index"/> in <paramref name="engine"/>, before it is deleted.</summary>
    public static DeletePhotoAction Capture(int scanIndex, int index, PhotoCropperEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        CropCandidate? candidate = index < engine.AcceptedCandidates.Count ? engine.AcceptedCandidates[index] : null;
        Mat? raw = index < engine.RawDetectedPhotos.Count ? engine.RawDetectedPhotos[index] : null;
        int rotation = engine.PhotoRotations.TryGetValue(index, out int r) ? r : 0;
        return new DeletePhotoAction(scanIndex, index, engine.DetectedPhotos[index], candidate, raw, rotation);
    }

    public void Undo(PhotoCropperEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (_isDisposed) return;

        engine.InsertPhoto(_index, _deletedMat.Clone(), _candidate, _deletedRaw, _rotation);
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
            _deletedRaw?.Dispose();
            _isDisposed = true;
        }
    }
}

internal sealed class RotatePhotoAction(int scanIndex, int index, bool clockwise = true) : IUndoableAction
{
    private readonly int _scanIndex = scanIndex;
    private readonly int _index = index;
    private readonly bool _clockwise = clockwise;

    public string Description => "Rotate Photo";
    public int ScanIndex => _scanIndex;

    public void Undo(PhotoCropperEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (_index < 0 || _index >= engine.DetectedPhotos.Count) return;

        // Rotate back through the engine (not the Mat directly) so the raw compare copy and the
        // persisted PhotoRotations entry are reverted together with the visible photo.
        if (_clockwise)
        {
            engine.RotatePhotoCounterClockwise(_index);
        }
        else
        {
            engine.RotatePhoto(_index);
        }
    }

    public void Redo(PhotoCropperEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (_index < 0 || _index >= engine.DetectedPhotos.Count) return;

        if (_clockwise)
        {
            engine.RotatePhoto(_index);
        }
        else
        {
            engine.RotatePhotoCounterClockwise(_index);
        }
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
    private readonly Mat _previousMat;
    private readonly Mat _newMat;
    private readonly Mat? _previousRaw;
    private readonly Mat? _newRaw;
    private bool _isDisposed;

    public string Description { get; }
    public int ScanIndex => _scanIndex;

    /// <param name="previousRaw">Optional raw (pre-restoration) copy before the change; restored on undo when given.</param>
    /// <param name="newRaw">Optional raw copy after the change; restored on redo when given.</param>
    public ReplacePhotoAction(int scanIndex, int index, Mat previousMat, Mat newMat, string description = "Crop Refinement", Mat? previousRaw = null, Mat? newRaw = null)
    {
        ArgumentNullException.ThrowIfNull(previousMat);
        ArgumentNullException.ThrowIfNull(newMat);
        _scanIndex = scanIndex;
        _index = index;
        _previousMat = previousMat.Clone();
        _newMat = newMat.Clone();
        _previousRaw = previousRaw?.Clone();
        _newRaw = newRaw?.Clone();
        Description = description;
    }

    public void Undo(PhotoCropperEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (_isDisposed || _index < 0 || _index >= engine.DetectedPhotos.Count) return;

        engine.DetectedPhotos[_index].Dispose();
        engine.DetectedPhotos[_index] = _previousMat.Clone();
        UndoRedoHelpers.ReplaceRaw(engine, _index, _previousRaw);
    }

    public void Redo(PhotoCropperEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (_isDisposed || _index < 0 || _index >= engine.DetectedPhotos.Count) return;

        engine.DetectedPhotos[_index].Dispose();
        engine.DetectedPhotos[_index] = _newMat.Clone();
        UndoRedoHelpers.ReplaceRaw(engine, _index, _newRaw);
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _previousMat.Dispose();
            _newMat.Dispose();
            _previousRaw?.Dispose();
            _newRaw?.Dispose();
            _isDisposed = true;
        }
    }
}

internal static class UndoRedoHelpers
{
    /// <summary>Replaces the engine's raw compare copy at <paramref name="index"/> with a clone of <paramref name="raw"/>, if given.</summary>
    public static void ReplaceRaw(PhotoCropperEngine engine, int index, Mat? raw)
    {
        if (raw == null || index < 0 || index >= engine.RawDetectedPhotos.Count) return;

        engine.RawDetectedPhotos[index].Dispose();
        engine.RawDetectedPhotos[index] = raw.Clone();
    }
}

internal sealed class AddPhotoAction : IUndoableAction
{
    private readonly int _scanIndex;
    private readonly int _index;
    private readonly Mat _addedMat;
    private readonly CropCandidate? _candidate;
    private bool _isDisposed;

    public string Description => "Add Manual Crop";
    public int ScanIndex => _scanIndex;

    public AddPhotoAction(int scanIndex, int index, Mat addedMat, CropCandidate? candidate = null)
    {
        ArgumentNullException.ThrowIfNull(addedMat);
        _scanIndex = scanIndex;
        _index = index;
        _addedMat = addedMat.Clone();
        _candidate = candidate;
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

        engine.InsertPhoto(_index, _addedMat.Clone(), _candidate);
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

/// <summary>
/// Records a grab-handle drag operation (bounding box rotate or vertex resize on the main scan view).
/// On undo/redo both the extracted <see cref="PhotoCropperEngine.DetectedPhotos"/> entry
/// and the full <see cref="PhotoCropperEngine.AcceptedCandidates"/> list are restored so that
/// both the cropped-photo thumbnail and the overlay bounding box return to the correct state.
/// </summary>
internal sealed class ResizeCandidateAction : IUndoableAction
{
    private readonly int _scanIndex;
    private readonly int _candIndex;
    private readonly Mat _previousMat;
    private readonly Mat _newMat;
    private readonly Mat? _previousRaw;
    private readonly Mat? _newRaw;
    private readonly IReadOnlyList<CropCandidate> _previousCandidates;
    private readonly IReadOnlyList<CropCandidate> _newCandidates;
    private bool _isDisposed;

    public string Description { get; }
    public int ScanIndex => _scanIndex;

    /// <param name="previousRaw">Optional raw (pre-restoration) copy before the drag. When omitted, the visible photo is used.</param>
    /// <param name="newRaw">Optional raw copy after the drag. When omitted, the visible photo is used.</param>
    public ResizeCandidateAction(
        int scanIndex,
        int candIndex,
        Mat previousMat,
        Mat newMat,
        IReadOnlyList<CropCandidate> previousCandidates,
        IReadOnlyList<CropCandidate> newCandidates,
        string description = "Adjust Crop Box",
        Mat? previousRaw = null,
        Mat? newRaw = null)
    {
        ArgumentNullException.ThrowIfNull(previousMat);
        ArgumentNullException.ThrowIfNull(newMat);
        ArgumentNullException.ThrowIfNull(previousCandidates);
        ArgumentNullException.ThrowIfNull(newCandidates);
        _scanIndex = scanIndex;
        _candIndex = candIndex;
        _previousMat = previousMat.Clone();
        _newMat = newMat.Clone();
        _previousRaw = previousRaw?.Clone();
        _newRaw = newRaw?.Clone();
        // Copy the lists: callers often pass the engine's live AcceptedCandidates, which later edits
        // replace or change, and the recorded undo/redo state must not move with them.
        _previousCandidates = [.. previousCandidates];
        _newCandidates = [.. newCandidates];
        Description = description;
    }

    public void Undo(PhotoCropperEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (_isDisposed || _candIndex < 0 || _candIndex >= engine.DetectedPhotos.Count) return;

        engine.DetectedPhotos[_candIndex].Dispose();
        engine.DetectedPhotos[_candIndex] = _previousMat.Clone();
        UndoRedoHelpers.ReplaceRaw(engine, _candIndex, _previousRaw ?? _previousMat);

        engine.UpdateCandidates(_previousCandidates);
    }

    public void Redo(PhotoCropperEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (_isDisposed || _candIndex < 0 || _candIndex >= engine.DetectedPhotos.Count) return;

        engine.DetectedPhotos[_candIndex].Dispose();
        engine.DetectedPhotos[_candIndex] = _newMat.Clone();
        UndoRedoHelpers.ReplaceRaw(engine, _candIndex, _newRaw ?? _newMat);

        engine.UpdateCandidates(_newCandidates);
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _previousMat.Dispose();
            _newMat.Dispose();
            _previousRaw?.Dispose();
            _newRaw?.Dispose();
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

    public void PushDelete(int scanIndex, int index, Mat photoMat, CropCandidate? candidate = null)
    {
        PushAction(new DeletePhotoAction(scanIndex, index, photoMat, candidate));
    }

    /// <summary>
    /// Records the deletion of photo <paramref name="index"/>, capturing its photo, raw copy, box and rotation
    /// from <paramref name="engine"/>. Call before deleting it.
    /// </summary>
    public void PushDelete(int scanIndex, int index, PhotoCropperEngine engine)
    {
        PushAction(DeletePhotoAction.Capture(scanIndex, index, engine));
    }

    public void PushRotate(int scanIndex, int index, bool clockwise = true)
    {
        PushAction(new RotatePhotoAction(scanIndex, index, clockwise));
    }

    public void PushReplace(int scanIndex, int index, Mat previousMat, Mat newMat, string description = "Crop Refinement", Mat? previousRaw = null, Mat? newRaw = null)
    {
        PushAction(new ReplacePhotoAction(scanIndex, index, previousMat, newMat, description, previousRaw, newRaw));
    }

    public void PushAdd(int scanIndex, int index, Mat addedMat, CropCandidate? candidate = null)
    {
        PushAction(new AddPhotoAction(scanIndex, index, addedMat, candidate));
    }

    public void PushBatch(int scanIndex, IReadOnlyList<IUndoableAction> actions, string description)
    {
        PushAction(new BatchAction(scanIndex, actions, description));
    }

    public void PushResizeCandidate(
        int scanIndex,
        int candIndex,
        Mat previousMat,
        Mat newMat,
        IReadOnlyList<CropCandidate> previousCandidates,
        IReadOnlyList<CropCandidate> newCandidates,
        string description = "Adjust Crop Box",
        Mat? previousRaw = null,
        Mat? newRaw = null)
    {
        PushAction(new ResizeCandidateAction(scanIndex, candIndex, previousMat, newMat, previousCandidates, newCandidates, description, previousRaw, newRaw));
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

    /// <summary>
    /// Removes and disposes all undo and redo entries that belong to the specified scan index.
    /// This must be called when a scan session's engine is deactivated and will be re-created
    /// from scratch, because any existing history entries would apply to a stale engine state.
    /// </summary>
    public void InvalidateScan(int scanIndex)
    {
        RemoveMatchingEntries(_undoList, scanIndex);
        RemoveMatchingEntries(_redoList, scanIndex);
    }

    private static void RemoveMatchingEntries(LinkedList<IUndoableAction> list, int scanIndex)
    {
        var node = list.First;
        while (node != null)
        {
            var next = node.Next;
            if (node.Value.ScanIndex == scanIndex)
            {
                list.Remove(node);
                node.Value.Dispose();
            }
            node = next;
        }
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
