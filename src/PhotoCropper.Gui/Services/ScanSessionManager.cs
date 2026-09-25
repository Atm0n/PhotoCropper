using PhotoCropper.Gui.Models;

namespace PhotoCropper.Gui.Services;

internal sealed class ScanSessionManager : IDisposable
{
    private readonly List<ScanSessionItem> _sessions = [];
    private bool _disposed;

    public IReadOnlyList<ScanSessionItem> Sessions => _sessions;

    public int Count => _sessions.Count;

    public bool HasScans => _sessions.Count > 0;

    public int CurrentIndex { get; private set; }

    public ScanSessionItem? CurrentSession =>
        HasScans && CurrentIndex >= 0 && CurrentIndex < _sessions.Count
            ? _sessions[CurrentIndex]
            : null;

    public bool MoveTo(int index)
    {
        if (!HasScans)
        {
            CurrentIndex = 0;
            return false;
        }

        if (index < 0 || index >= _sessions.Count)
        {
            return false;
        }

        if (index != CurrentIndex)
        {
            CurrentSession?.TryDeactivateIfUnmodified();
            CurrentIndex = index;
        }

        return true;
    }

    public bool MoveNext()
    {
        if (!HasScans) return false;
        CurrentSession?.TryDeactivateIfUnmodified();
        CurrentIndex = (CurrentIndex + 1) % _sessions.Count;
        return true;
    }

    public bool MovePrevious()
    {
        if (!HasScans) return false;
        CurrentSession?.TryDeactivateIfUnmodified();
        CurrentIndex = (CurrentIndex - 1 + _sessions.Count) % _sessions.Count;
        return true;
    }

    public void Add(ScanSessionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _sessions.Add(item);
        if (_sessions.Count == 1)
        {
            CurrentIndex = 0;
        }
    }

    public void AddRange(IEnumerable<ScanSessionItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items)
        {
            _sessions.Add(item);
        }

        if (_sessions.Count > 0 && CurrentIndex >= _sessions.Count)
        {
            CurrentIndex = 0;
        }
    }

    public void ReplaceAll(IEnumerable<ScanSessionItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Clear();
        foreach (var item in items)
        {
            _sessions.Add(item);
        }
        CurrentIndex = 0;
    }

    public ScanSessionItem? RemoveCurrent()
    {
        if (!HasScans) return null;
        return RemoveAt(CurrentIndex);
    }

    public ScanSessionItem? RemoveAt(int index)
    {
        if (index < 0 || index >= _sessions.Count) return null;

        var item = _sessions[index];
        item.Dispose();
        _sessions.RemoveAt(index);

        if (_sessions.Count == 0)
        {
            CurrentIndex = 0;
        }
        else if (CurrentIndex >= _sessions.Count)
        {
            CurrentIndex = _sessions.Count - 1;
        }

        return item;
    }

    public bool HasUnsavedChanges()
    {
        return _sessions.Any(s => !s.IsSaved || s.IsModified);
    }

    public IReadOnlyList<ScanSessionItem> GetPendingExportSessions()
    {
        return _sessions.Where(s => !s.IsSaved || s.IsModified).ToList();
    }

    public ScanSessionItem? FindByPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return null;
        return _sessions.FirstOrDefault(s => string.Equals(s.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
    }

    public void Clear()
    {
        foreach (var session in _sessions)
        {
            session.Dispose();
        }
        _sessions.Clear();
        CurrentIndex = 0;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Clear();
            _disposed = true;
        }
    }
}
