using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoCropper.Core;
using PhotoCropper.Gui.Services;
using System.Collections.ObjectModel;

namespace PhotoCropper.Gui.ViewModels;

/// <summary>One photo of the open scan, in the single-photo carousel and the grid.</summary>
internal sealed partial class CropPhotoItem(int index, Bitmap image, string label, string dimensions, Action<CropPhotoItem> selectionChanged) : ObservableObject
{
    public int Index { get; } = index;
    public string Label { get; } = label;
    public string Dimensions { get; } = dimensions;

    /// <summary>The photo, or its unrestored original while "hold to compare" is held.</summary>
    [ObservableProperty] private Bitmap _image = image;

    /// <summary>Selected in the grid (several photos can be).</summary>
    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => selectionChanged(this);
}

/// <summary>
/// The photos found on the open scan: shown one at a time (carousel) or as a grid with multi-selection, with
/// delete and rotate for the selected photos (undoable), the photo counter and the selection-dependent labels.
/// </summary>
internal sealed partial class CropsStageViewModel
{
    private bool _syncingSelection;
    private bool _showingOriginal;

    public ObservableCollection<CropPhotoItem> Photos { get; } = [];

    /// <summary>The photo selected last (the one shown by the counter, refined, or reviewed first).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousPhotoCommand), nameof(NextPhotoCommand))]
    private int _selectedPhotoIndex = -1;

    [ObservableProperty] private string _photoCounterText = string.Empty;
    [ObservableProperty] private string _selectionText = string.Empty;
    [ObservableProperty] private bool _isSelectionTextVisible;
    [ObservableProperty] private string _deleteButtonText = string.Empty;
    [ObservableProperty] private string _rotateButtonText = string.Empty;
    [ObservableProperty] private bool _canRefine;
    [ObservableProperty] private string _refineTip = string.Empty;
    [ObservableProperty] private bool _hasPhotos;

    /// <summary>The photo grid is shown (there are photos).</summary>
    public bool IsGridVisible => HasPhotos;

    partial void OnHasPhotosChanged(bool value) => OnPropertyChanged(nameof(IsGridVisible));

    partial void OnSelectedPhotoIndexChanged(int value)
    {
        if (!_syncingSelection && value >= 0) SelectOnly(value);
        RefreshPhotoTexts();
        // Only the selected box has handles: repaint the boxes.
        if (OpenEngine is { } engine) _host.ShowBoxes(engine);
    }

    /// <summary>The selected photo's box (its index among the scan's boxes), or -1: the box that shows its handles.</summary>
    public int SelectedBoxIndex => SelectedPhotoIndex >= 0 && SelectedPhotoIndex < Photos.Count ? Photos[SelectedPhotoIndex].Index : -1;

    /// <summary>Rebuilds the photo list from the open scan and selects <paramref name="select"/> (clamped).</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Each bitmap is owned by its CropPhotoItem and disposed (via DisposeLater) when the list is rebuilt or cleared")]
    public void ShowPhotos(int select = 0)
    {
        _showingOriginal = false;
        var old = Photos.Select(p => (IDisposable)p.Image).ToList();
        _syncingSelection = true;
        try
        {
            Photos.Clear();
            if (Current?.Activate() is { } engine)
            {
                for (int i = 0; i < engine.DetectedPhotos.Count; i++)
                {
                    var mat = engine.DetectedPhotos[i];
                    if (mat == null || mat.Empty() || mat.Width <= 0 || mat.Height <= 0) continue;
                    Photos.Add(new CropPhotoItem(i, MatBitmapConverter.ToAvaloniaBitmap(mat), $"#{i + 1}", $"{mat.Width} × {mat.Height} px", OnItemSelectionChanged));
                }
            }
            SelectedPhotoIndex = -1;
        }
        finally
        {
            _syncingSelection = false;
        }

        // Disposed after the carousel's slide animation has stopped drawing them.
        _host.DisposeLater(old);
        HasPhotos = Photos.Count > 0;
        SelectPhoto(select);
        RefreshPhotoTexts();
    }

    /// <summary>Empties the photo list (no scan open).</summary>
    public void ClearPhotos()
    {
        var old = Photos.Select(p => (IDisposable)p.Image).ToList();
        _syncingSelection = true;
        try
        {
            Photos.Clear();
            SelectedPhotoIndex = -1;
        }
        finally
        {
            _syncingSelection = false;
        }
        _host.DisposeLater(old);
        HasPhotos = false;
        RefreshPhotoTexts();
    }

    /// <summary>Shows one photo (and makes it the only one selected in the grid).</summary>
    public void SelectPhoto(int index)
    {
        if (Photos.Count == 0) return;
        SelectedPhotoIndex = Math.Clamp(index, 0, Photos.Count - 1);
    }

    /// <summary>Selects exactly these photos in the grid (e.g. after rotating them).</summary>
    public void SelectPhotos(IEnumerable<int> indices)
    {
        var set = indices.ToHashSet();
        _syncingSelection = true;
        try
        {
            foreach (var item in Photos) item.IsSelected = set.Contains(item.Index);
        }
        finally
        {
            _syncingSelection = false;
        }
        RefreshPhotoTexts();
    }

    /// <summary>A context menu was opened on <paramref name="item"/>: act on it unless it is part of the selection.</summary>
    public void EnsureSelected(CropPhotoItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.IsSelected) SelectPhoto(Photos.IndexOf(item));
    }

    /// <summary>The photos the actions apply to: the grid's selection.</summary>
    public IReadOnlyList<int> SelectedPhotoIndices() => [.. Photos.Where(p => p.IsSelected).Select(p => p.Index)];

    private void SelectOnly(int position)
    {
        _syncingSelection = true;
        try
        {
            for (int i = 0; i < Photos.Count; i++) Photos[i].IsSelected = i == position;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void OnItemSelectionChanged(CropPhotoItem item)
    {
        if (_syncingSelection) return;
        var selected = Photos.Where(p => p.IsSelected).ToList();
        if (selected.Count == 1)
        {
            _syncingSelection = true;
            try
            {
                SelectedPhotoIndex = Photos.IndexOf(selected[0]);
            }
            finally
            {
                _syncingSelection = false;
            }
        }
        RefreshPhotoTexts();
    }

    [RelayCommand]
    private void SelectAll() => SelectPhotos(Photos.Select(p => p.Index));

    [RelayCommand]
    private void ClearSelection() => SelectPhotos([]);

    private bool CanGoToPreviousPhoto() => SelectedPhotoIndex > 0;
    private bool CanGoToNextPhoto() => SelectedPhotoIndex >= 0 && SelectedPhotoIndex < Photos.Count - 1;

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPhoto))]
    private void PreviousPhoto() => SelectPhoto(SelectedPhotoIndex - 1);

    [RelayCommand(CanExecute = nameof(CanGoToNextPhoto))]
    private void NextPhoto() => SelectPhoto(SelectedPhotoIndex + 1);

    /// <summary>"Hold to compare": shows the selected photo as extracted, before orientation fixes and restoration.</summary>
    public void ShowOriginal(bool original)
    {
        if (original == _showingOriginal || SelectedPhotoIndex < 0 || SelectedPhotoIndex >= Photos.Count) return;
        if (Current?.Engine is not { } engine) return;

        var item = Photos[SelectedPhotoIndex];
        var source = original ? engine.RawDetectedPhotos : engine.DetectedPhotos;
        if (item.Index >= source.Count) return;

        var old = item.Image;
        item.Image = MatBitmapConverter.ToAvaloniaBitmap(source[item.Index]);
        _host.DisposeLater([old]);
        _showingOriginal = original;
    }

    /// <summary>Deletes the selected photos (one undo step).</summary>
    [RelayCommand]
    public void DeleteSelectedPhotos()
    {
        if (!CanStartWork) return;
        var engine = Current!.Activate();
        var indices = SelectedPhotoIndices().Distinct()
            .Where(i => i >= 0 && i < engine.DetectedPhotos.Count)
            .OrderByDescending(i => i)
            .ToList();
        if (indices.Count == 0) return;

        Current.IsModified = true;
        int scan = _sessions.CurrentIndex;
        if (indices.Count == 1)
        {
            _undo.PushDelete(scan, indices[0], engine);
            engine.DeletePhoto(indices[0]);
        }
        else
        {
            var actions = new List<IUndoableAction>();
            foreach (int i in indices)
            {
                actions.Add(DeletePhotoAction.Capture(scan, i, engine));
                engine.DeletePhoto(i);
            }
            _undo.PushBatch(scan, actions, $"Batch Delete ({indices.Count} photos)");
        }

        Present(engine, select: indices.Min());
        _host.ShowStatus(indices.Count == 1
            ? LocalizationService.GetString(ResourceKeys.MsgPhotoDeleted, "Photo deleted.")
            : LocalizationService.Format(ResourceKeys.MsgBatchDeleted, "{0} photos deleted.", indices.Count));
    }

    [RelayCommand]
    public Task RotateSelectedPhotosAsync() => RotateSelectedAsync(clockwise: true);

    [RelayCommand]
    public Task RotateSelectedPhotosCounterClockwiseAsync() => RotateSelectedAsync(clockwise: false);

    /// <summary>Turns the selected photos a quarter turn (one undo step) and keeps them selected.</summary>
    private async Task RotateSelectedAsync(bool clockwise)
    {
        if (!CanStartWork) return;
        var session = Current!;
        var engine = session.Activate();
        var indices = SelectedPhotoIndices().Distinct()
            .Where(i => i >= 0 && i < engine.DetectedPhotos.Count)
            .Order()
            .ToList();
        if (indices.Count == 0) return;

        session.IsModified = true;
        int scan = _sessions.CurrentIndex;
        int shown = SelectedPhotoIndex;
        string message = indices.Count == 1
            ? LocalizationService.GetString(ResourceKeys.MsgRotating, "Rotating...")
            : LocalizationService.Format(ResourceKeys.MsgBatchRotating, "Rotating {0} photos...", indices.Count);

        bool done = await _host.RunBusyAsync(message, async ct =>
        {
            await Task.Run(() =>
            {
                foreach (int i in indices)
                {
                    ct.ThrowIfCancellationRequested();
                    if (clockwise) engine.RotatePhoto(i);
                    else engine.RotatePhotoCounterClockwise(i);
                }
            }, ct);

            if (indices.Count == 1)
            {
                _undo.PushRotate(scan, indices[0], clockwise);
            }
            else
            {
                _undo.PushBatch(scan, [.. indices.Select(i => (IUndoableAction)new RotatePhotoAction(scan, i, clockwise))],
                    clockwise ? $"Batch Rotate ({indices.Count} photos)" : $"Batch Rotate CCW ({indices.Count} photos)");
            }

            ShowPhotos(shown >= 0 ? shown : indices[0]);
            if (indices.Count > 1) SelectPhotos(indices);
        });

        if (done)
        {
            _host.ShowStatus(indices.Count == 1
                ? LocalizationService.GetString(ResourceKeys.MsgPhotoRotated, "Photo rotated.")
                : LocalizationService.Format(ResourceKeys.MsgBatchRotated, "{0} photos rotated.", indices.Count));
        }
    }

    /// <summary>Shows a changed scan everywhere: image and boxes, the photo list, and coverage.</summary>
    private void Present(PhotoCropperEngine engine, int select = 0)
    {
        _host.ShowScanResult(engine);
        ShowPhotos(select);
        UpdateCoverage();
    }

    /// <summary>Refreshes the labels that depend on the photos and their selection (also after a language change).</summary>
    public void RefreshPhotoTexts()
    {
        int total = Photos.Count;
        int selected = Photos.Count(p => p.IsSelected);

        PhotoCounterText = total == 0 || SelectedPhotoIndex < 0
            ? string.Empty
            : LocalizationService.Format(ResourceKeys.PhotoCounter, "PHOTO {0} OF {1}", SelectedPhotoIndex + 1, total);

        bool several = selected > 1;
        IsSelectionTextVisible = several;
        SelectionText = several ? LocalizationService.Format(ResourceKeys.TxtSelectedCount, "({0} of {1} selected)", selected, total) : string.Empty;
        DeleteButtonText = several
            ? LocalizationService.Format(ResourceKeys.BtnDeleteCount, "Delete {0} photos", selected)
            : LocalizationService.GetString(ResourceKeys.BtnDeletePhoto, "Delete photo");
        RotateButtonText = several
            ? LocalizationService.Format(ResourceKeys.BtnRotateCount, "Rotate ({0})", selected)
            : LocalizationService.GetString(ResourceKeys.BtnRotate, "Rotate");
        CanRefine = !several && total > 0 && selected > 0;
        RefineTip = several
            ? LocalizationService.GetString(ResourceKeys.TipRefineMultiDisabled, "Select a single photo to refine edges")
            : LocalizationService.GetString(ResourceKeys.TipRefine, "Straighten and tighten the selected photo's edges");

        PreviousPhotoCommand.NotifyCanExecuteChanged();
        NextPhotoCommand.NotifyCanExecuteChanged();
    }
}
