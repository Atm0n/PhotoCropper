using Avalonia.Input;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;

namespace PhotoCropper.Gui.ViewModels;

/// <summary>One photo in review: which scan, and which of its photos.</summary>
internal sealed record ReviewPhotoItem(int ScanIndex, int PhotoIndex);

/// <summary>What review mode needs from the window.</summary>
internal interface IReviewHost
{
    IReadOnlyList<ScanSessionItem> Scans { get; }

    /// <summary>The scan open in the Crops editor.</summary>
    int CurrentScanIndex { get; }

    /// <summary>Disposes bitmaps once the UI has stopped drawing them.</summary>
    void DisposeLater(IReadOnlyCollection<IDisposable> items);

    /// <summary>Review closed on this photo (-1, -1 when none is left): the editor should show it.</summary>
    void ReviewClosed(int scanIndex, int photoIndex);

    void ShowError(string title, string message);
}

/// <summary>
/// Review mode: steps through every photo of every scan, full size, to rotate or delete them quickly. After the last
/// photo comes an end slide with what was done, then it wraps around. Changes on the scan open in the editor can be
/// undone there.
/// </summary>
internal sealed partial class ReviewViewModel(IReviewHost host, UndoRedoHistory undo) : ObservableObject
{
    private readonly List<ReviewPhotoItem> _items = [];
    private int _position = -1;
    private int _rotations;
    private int _deletions;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private Bitmap? _image;

    /// <summary>Showing the end slide (after the last photo) instead of a photo.</summary>
    [ObservableProperty] private bool _isAtEnd;

    [ObservableProperty] private string _counterText = string.Empty;
    [ObservableProperty] private string _scanInfoText = string.Empty;
    [ObservableProperty] private string _endStatsText = string.Empty;

    public IReadOnlyList<ReviewPhotoItem> Items => _items;

    /// <summary>Every photo of every scan, in order, and where <paramref name="selectedPhotoIndex"/> of the current scan sits.</summary>
    public static IReadOnlyList<ReviewPhotoItem> Flatten(IReadOnlyList<ScanSessionItem> sessions, int currentIndex, out int initialReviewIndex, int selectedPhotoIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var items = new List<ReviewPhotoItem>();
        initialReviewIndex = 0;

        for (int s = 0; s < sessions.Count; s++)
        {
            int count = sessions[s].PhotoCount;
            for (int p = 0; p < count; p++)
            {
                if (s == currentIndex && p == selectedPhotoIndex) initialReviewIndex = items.Count;
                items.Add(new ReviewPhotoItem(s, p));
            }
        }
        return items;
    }

    /// <summary>Opens review on <paramref name="selectedPhotoIndex"/> of the current scan. False when there is nothing to review.</summary>
    public bool Open(int selectedPhotoIndex)
    {
        var scans = host.Scans;
        if (scans.Count == 0) return false;

        // The current scan is loaded so its count is exact; others use their last known count.
        if (host.CurrentScanIndex >= 0 && host.CurrentScanIndex < scans.Count) scans[host.CurrentScanIndex].Activate();

        _items.Clear();
        _items.AddRange(Flatten(scans, host.CurrentScanIndex, out int initial, selectedPhotoIndex));
        if (_items.Count == 0)
        {
            host.ShowError(
                LocalizationService.GetString(ResourceKeys.TitleReviewMode, "Photo Review"),
                LocalizationService.GetString(ResourceKeys.MsgReviewEmpty, "No detected photos to review across any scans."));
            return false;
        }

        _rotations = 0;
        _deletions = 0;
        _position = initial;
        IsOpen = true;
        ShowCurrent();
        return true;
    }

    /// <summary>Closes review and tells the editor which photo it ended on.</summary>
    [RelayCommand]
    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        IsAtEnd = false;
        ReplaceImage(null);

        int last = Math.Min(_position, _items.Count - 1);
        if (last >= 0) host.ReviewClosed(_items[last].ScanIndex, _items[last].PhotoIndex);
        else host.ReviewClosed(-1, -1);
    }

    // Positions 0..Count-1 are photos, Count is the end slide.
    [RelayCommand]
    public void Next()
    {
        if (_items.Count == 0) return;
        _position = (_position + 1) % (_items.Count + 1);
        ShowCurrent();
    }

    [RelayCommand]
    public void Previous()
    {
        if (_items.Count == 0) return;
        _position = (_position - 1 + _items.Count + 1) % (_items.Count + 1);
        ShowCurrent();
    }

    [RelayCommand]
    public void RotateClockwise() => Rotate(clockwise: true);

    [RelayCommand]
    public void RotateCounterClockwise() => Rotate(clockwise: false);

    private void Rotate(bool clockwise)
    {
        if (!TryGetCurrent(out var item, out var session, out var engine)) return;

        session.IsModified = true;
        if (clockwise) engine.RotatePhoto(item.PhotoIndex);
        else engine.RotatePhotoCounterClockwise(item.PhotoIndex);
        _rotations++;
        if (item.ScanIndex == host.CurrentScanIndex) undo.PushRotate(item.ScanIndex, item.PhotoIndex, clockwise);
        ShowCurrent();
    }

    /// <summary>Deletes the photo shown; the next one takes its place. Review closes when none is left.</summary>
    [RelayCommand]
    public void Delete()
    {
        if (!TryGetCurrent(out var item, out var session, out var engine)) return;

        session.IsModified = true;
        if (item.ScanIndex == host.CurrentScanIndex) undo.PushDelete(item.ScanIndex, item.PhotoIndex, engine);
        engine.DeletePhoto(item.PhotoIndex);
        _deletions++;

        // Later photos of the same scan move up one place.
        _items.RemoveAt(_position);
        for (int i = 0; i < _items.Count; i++)
        {
            var other = _items[i];
            if (other.ScanIndex == item.ScanIndex && other.PhotoIndex > item.PhotoIndex)
            {
                _items[i] = other with { PhotoIndex = other.PhotoIndex - 1 };
            }
        }

        if (_items.Count == 0)
        {
            Close();
            return;
        }
        if (_position > _items.Count) _position = _items.Count - 1;
        ShowCurrent();
    }

    /// <summary>Review's shortcuts. While review is open it takes every key, so the editor underneath gets none.</summary>
    public bool HandleKey(Key key)
    {
        if (!IsOpen) return false;
        switch (key)
        {
            case Key.Escape or Key.E: Close(); break;
            case Key.R: RotateClockwise(); break;
            case Key.L: RotateCounterClockwise(); break;
            case Key.Right or Key.Space or Key.Enter: Next(); break;
            case Key.Left or Key.Back: Previous(); break;
            case Key.Delete or Key.X: Delete(); break;
        }
        return true;
    }

    private bool TryGetCurrent(out ReviewPhotoItem item, out ScanSessionItem session, out Core.PhotoCropperEngine engine)
    {
        item = null!;
        session = null!;
        engine = null!;
        if (!IsOpen || _position < 0 || _position >= _items.Count) return false;

        item = _items[_position];
        if (item.ScanIndex >= host.Scans.Count) return false;
        session = host.Scans[item.ScanIndex];
        engine = session.Activate();
        return item.PhotoIndex < engine.DetectedPhotos.Count;
    }

    private void ShowCurrent()
    {
        if (_items.Count == 0 || _position < 0)
        {
            ReplaceImage(null);
            CounterText = string.Empty;
            ScanInfoText = string.Empty;
            return;
        }

        if (_position == _items.Count)
        {
            ShowEnd();
            return;
        }

        IsAtEnd = false;
        var item = _items[_position];
        var session = host.Scans[item.ScanIndex];
        var engine = session.Activate();
        if (item.PhotoIndex >= engine.DetectedPhotos.Count)
        {
            // The scan has fewer photos than counted (e.g. it was re-detected): drop the missing ones.
            _items.RemoveAll(i => i.ScanIndex == item.ScanIndex && i.PhotoIndex >= engine.DetectedPhotos.Count);
            _position = Math.Clamp(_position, 0, _items.Count);
            ShowCurrent();
            return;
        }

        var photo = engine.DetectedPhotos[item.PhotoIndex];
        ReplaceImage(MatBitmapConverter.ToAvaloniaBitmap(photo));
        CounterText = LocalizationService.Format(ResourceKeys.PhotoCounter, "PHOTO {0} OF {1}", _position + 1, _items.Count);
        ScanInfoText = $"{Path.GetFileName(session.FilePath)} • {photo.Width} × {photo.Height} px";
    }

    private void ShowEnd()
    {
        IsAtEnd = true;
        ReplaceImage(null);
        int scans = _items.Select(i => i.ScanIndex).Distinct().Count();
        EndStatsText = LocalizationService.Format(ResourceKeys.ReviewSentinelStats, "{0} photos · {1} scans · {2} rotated · {3} deleted",
            _items.Count, scans, _rotations, _deletions);
        CounterText = LocalizationService.Format(ResourceKeys.PhotoCounter, "PHOTO {0} OF {1}", _items.Count, _items.Count);
        ScanInfoText = LocalizationService.GetString(ResourceKeys.ReviewSentinelSubtitle, "All photos reviewed");
    }

    private void ReplaceImage(Bitmap? image)
    {
        var old = Image;
        Image = image;
        if (old != null) host.DisposeLater([old]);
    }
}
