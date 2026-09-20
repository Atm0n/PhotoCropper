using Avalonia.Controls;
using Avalonia.Interactivity;
using PhotoCropper.Gui.Services;

namespace PhotoCropper.Gui;

internal sealed partial class MainWindow
{
    internal void ClearGalleryBitmaps()
    {
        var disposedBitmaps = new HashSet<IDisposable>();

        if (slides != null)
        {
            foreach (var item in slides.Items)
            {
                if (item is IDisposable disposable && disposedBitmaps.Add(disposable))
                {
                    disposable.Dispose();
                }
            }
            slides.Items.Clear();
        }

        if (lstGallery != null)
        {
            foreach (var item in lstGallery.Items)
            {
                if (item is GalleryPhotoItem galleryItem && galleryItem.Image is IDisposable disposable && disposedBitmaps.Add(disposable))
                {
                    disposable.Dispose();
                }
            }
            lstGallery.Items.Clear();
        }
    }

    private void LoadCroppedPhotosToSlider()
    {
        ClearGalleryBitmaps();

        var detected = ScanSessions[currentIndex].Activate().DetectedPhotos;
        for (int i = 0; i < detected.Count; i++)
        {
            var mat = detected[i];
            if (mat == null || mat.IsEmpty || mat.Width <= 0 || mat.Height <= 0) continue;
            var bmp = MatBitmapConverter.ToAvaloniaBitmap(mat);
            slides.Items.Add(bmp);

            if (lstGallery != null)
            {
                string label = $"#{i + 1}";
                string dims = $"{mat.Width} × {mat.Height} px";
                lstGallery.Items.Add(new GalleryPhotoItem(bmp, label, dims, i));
            }
        }

        if (slides.Items.Count > 0)
        {
            slides.SelectedIndex = 0;
            if (lstGallery != null && lstGallery.Items.Count > 0)
            {
                lstGallery.SelectedIndex = 0;
            }
        }

        UpdateSelectionUi();
    }

    private void BtnDelete_Click(object? sender, RoutedEventArgs e)
    {
        if (isLoading) return;
        DeleteSelectedPhotos();
    }

    private void DeleteCurrentPhoto() => DeleteSelectedPhotos();

    private void DeleteSelectedPhotos()
    {
        if (isLoading || ScanSessions.Count == 0) return;

        var selectedIndices = GetSelectedPhotoIndices();
        if (selectedIndices.Count == 0) return;

        if (selectedIndices.Count == 1)
        {
            DeleteSinglePhoto(selectedIndices[0]);
            return;
        }

        BatchDeletePhotos(selectedIndices);
    }

    private void DeleteSinglePhoto(int photoIndex)
    {
        if (isLoading || ScanSessions.Count == 0 || photoIndex < 0) return;

        ScanSessions[currentIndex].IsModified = true;
        var currentEngine = ScanSessions[currentIndex].Activate();
        if (photoIndex >= currentEngine.DetectedPhotos.Count) return;

        var matToDelete = currentEngine.DetectedPhotos[photoIndex];
        undoHistory.PushDelete(currentIndex, photoIndex, matToDelete);

        currentEngine.DeletePhoto(photoIndex);

        int nextIndex = Math.Min(photoIndex, currentEngine.DetectedPhotos.Count - 1);
        LoadCroppedPhotosToSlider();
        if (nextIndex >= 0 && slides != null)
        {
            slides.SelectedIndex = nextIndex;
            if (lstGallery != null && nextIndex < lstGallery.Items.Count)
            {
                lstGallery.SelectedIndex = nextIndex;
            }
        }
        UpdateSelectionUi();
        lblStatus.Text = Avalonia.Application.Current?.FindResource("MsgPhotoDeleted")?.ToString() ?? "Photo deleted.";
    }

    private void BatchDeletePhotos(List<int> selectedIndices)
    {
        if (isLoading || ScanSessions.Count == 0 || selectedIndices.Count == 0) return;

        var currentEngine = ScanSessions[currentIndex].Activate();
        var sortedIndices = selectedIndices.Distinct()
            .Where(idx => idx >= 0 && idx < currentEngine.DetectedPhotos.Count)
            .OrderByDescending(idx => idx)
            .ToList();

        if (sortedIndices.Count == 0) return;

        ScanSessions[currentIndex].IsModified = true;

        var actions = new List<IUndoableAction>();
        int minIndex = sortedIndices.Min();

        foreach (var idx in sortedIndices)
        {
            var mat = currentEngine.DetectedPhotos[idx];
            actions.Add(new DeletePhotoAction(currentIndex, idx, mat));
            currentEngine.DeletePhoto(idx);
        }

        string desc = $"Batch Delete ({sortedIndices.Count} photos)";
        undoHistory.PushBatch(currentIndex, actions, desc);

        LoadCroppedPhotosToSlider();

        int nextIndex = Math.Clamp(minIndex, 0, currentEngine.DetectedPhotos.Count - 1);
        if (nextIndex >= 0 && nextIndex < currentEngine.DetectedPhotos.Count && slides != null)
        {
            slides.SelectedIndex = nextIndex;
            if (lstGallery != null && nextIndex < lstGallery.Items.Count)
            {
                lstGallery.SelectedIndex = nextIndex;
            }
        }

        UpdateSelectionUi();
        string format = Avalonia.Application.Current?.FindResource("MsgBatchDeleted")?.ToString() ?? "{0} photos deleted.";
        lblStatus.Text = string.Format(format, sortedIndices.Count);
    }

    private async void BtnRotate_Click(object? sender, RoutedEventArgs e)
    {
        if (isLoading) return;
        await RotateSelectedPhotosAsync();
    }

    private Task RotateCurrentPhotoAsync() => RotateSelectedPhotosAsync();

    private async Task RotateSelectedPhotosAsync()
    {
        if (isLoading || ScanSessions.Count == 0) return;

        var selectedIndices = GetSelectedPhotoIndices();
        if (selectedIndices.Count == 0) return;

        if (selectedIndices.Count == 1)
        {
            await RotateSinglePhotoAsync(selectedIndices[0]);
            return;
        }

        await BatchRotatePhotosAsync(selectedIndices);
    }

    private async Task RotateSinglePhotoAsync(int photoIndex)
    {
        if (isLoading || ScanSessions.Count == 0 || photoIndex < 0) return;

        ScanSessions[currentIndex].IsModified = true;
        undoHistory.PushRotate(currentIndex, photoIndex);

        string rotatingMsg = Avalonia.Application.Current?.FindResource("MsgRotating")?.ToString() ?? "Rotating...";
        string rotatedMsg = Avalonia.Application.Current?.FindResource("MsgPhotoRotated")?.ToString() ?? "Photo rotated.";

        await ExecuteWithLoadingAsync(rotatingMsg, async () =>
        {
            await Task.Run(() => ScanSessions[currentIndex].Activate().RotatePhoto(photoIndex));
            LoadCroppedPhotosToSlider();
            if (slides != null) slides.SelectedIndex = photoIndex;
            if (lstGallery != null && photoIndex < lstGallery.Items.Count)
            {
                lstGallery.SelectedIndex = photoIndex;
            }
            UpdateSelectionUi();
        }, rotatedMsg);
    }

    private async Task BatchRotatePhotosAsync(List<int> selectedIndices)
    {
        if (isLoading || ScanSessions.Count == 0 || selectedIndices.Count == 0) return;

        var currentEngine = ScanSessions[currentIndex].Activate();
        var validIndices = selectedIndices.Distinct()
            .Where(idx => idx >= 0 && idx < currentEngine.DetectedPhotos.Count)
            .OrderBy(idx => idx)
            .ToList();

        if (validIndices.Count == 0) return;

        ScanSessions[currentIndex].IsModified = true;

        var actions = new List<IUndoableAction>();
        foreach (var idx in validIndices)
        {
            actions.Add(new RotatePhotoAction(currentIndex, idx));
        }

        string desc = $"Batch Rotate ({validIndices.Count} photos)";
        undoHistory.PushBatch(currentIndex, actions, desc);

        string rotatingFormat = Avalonia.Application.Current?.FindResource("MsgBatchRotating")?.ToString() ?? "Rotating {0} photos...";
        string rotatedFormat = Avalonia.Application.Current?.FindResource("MsgBatchRotated")?.ToString() ?? "{0} photos rotated.";

        await ExecuteWithLoadingAsync(string.Format(rotatingFormat, validIndices.Count), async () =>
        {
            await Task.Run(() =>
            {
                foreach (var idx in validIndices)
                {
                    currentEngine.RotatePhoto(idx);
                }
            });

            LoadCroppedPhotosToSlider();

            if (lstGallery?.SelectedItems != null)
            {
                isSyncingSelection = true;
                try
                {
                    lstGallery.SelectedItems.Clear();
                    foreach (var idx in validIndices)
                    {
                        if (idx < lstGallery.Items.Count)
                        {
                            lstGallery.SelectedItems.Add(lstGallery.Items[idx]);
                        }
                    }
                }
                finally
                {
                    isSyncingSelection = false;
                }
            }
            UpdateSelectionUi();
        }, string.Format(rotatedFormat, validIndices.Count));
    }

    private void Slides_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdatePhotoCounterLabel();
        if (isSyncingSelection || lstGallery == null || slides == null) return;
        if (slides.SelectedIndex >= 0 && slides.SelectedIndex < lstGallery.Items.Count && lstGallery.SelectedIndex != slides.SelectedIndex)
        {
            isSyncingSelection = true;
            try
            {
                lstGallery.SelectedIndex = slides.SelectedIndex;
                lstGallery.ScrollIntoView(slides.SelectedIndex);
            }
            finally
            {
                isSyncingSelection = false;
            }
        }
        UpdateSelectionUi();
    }

    private void LstGallery_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (isSyncingSelection || lstGallery == null || slides == null) return;

        UpdateSelectionUi();

        if (lstGallery.SelectedItems?.Count == 1 && lstGallery.SelectedIndex >= 0 && lstGallery.SelectedIndex < slides.Items.Count && slides.SelectedIndex != lstGallery.SelectedIndex)
        {
            isSyncingSelection = true;
            try
            {
                slides.SelectedIndex = lstGallery.SelectedIndex;
            }
            finally
            {
                isSyncingSelection = false;
            }
            UpdatePhotoCounterLabel();
        }
    }

    private void RbViewMode_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (pnlCarouselView == null || scrollGalleryView == null) return;
        bool isGrid = rbViewGrid?.IsChecked == true;
        pnlCarouselView.IsVisible = !isGrid;
        scrollGalleryView.IsVisible = isGrid;

        if (isGrid && lstGallery != null && slides != null && slides.SelectedIndex >= 0)
        {
            lstGallery.SelectedIndex = slides.SelectedIndex;
            lstGallery.ScrollIntoView(slides.SelectedIndex);
        }
        else if (!isGrid && lstGallery != null && slides != null)
        {
            if (lstGallery.SelectedIndex >= 0 && lstGallery.SelectedIndex < slides.Items.Count)
            {
                slides.SelectedIndex = lstGallery.SelectedIndex;
            }
        }
        UpdateSelectionUi();
    }

    private void SetViewMode(bool gridView)
    {
        if (rbViewGrid != null && rbViewCarousel != null)
        {
            if (gridView) rbViewGrid.IsChecked = true;
            else rbViewCarousel.IsChecked = true;
        }
    }

    private void BtnSelectAll_Click(object? sender, RoutedEventArgs e)
    {
        SelectAllGalleryPhotos();
    }

    private void BtnClearSelection_Click(object? sender, RoutedEventArgs e)
    {
        ClearGallerySelection();
    }

    private void SelectAllGalleryPhotos()
    {
        if (lstGallery?.SelectedItems == null || lstGallery.Items.Count == 0) return;

        isSyncingSelection = true;
        try
        {
            lstGallery.SelectedItems.Clear();
            foreach (var item in lstGallery.Items)
            {
                lstGallery.SelectedItems.Add(item);
            }
        }
        finally
        {
            isSyncingSelection = false;
        }
        UpdateSelectionUi();
    }

    private void ClearGallerySelection()
    {
        if (lstGallery?.SelectedItems == null) return;

        isSyncingSelection = true;
        try
        {
            lstGallery.SelectedItems.Clear();
        }
        finally
        {
            isSyncingSelection = false;
        }
        UpdateSelectionUi();
    }

    private List<int> GetSelectedPhotoIndices()
    {
        if (rbViewGrid?.IsChecked == true)
        {
            if (lstGallery?.SelectedItems == null || lstGallery.SelectedItems.Count == 0)
            {
                return [];
            }

            var indices = new List<int>();
            foreach (var item in lstGallery.SelectedItems)
            {
                if (item is GalleryPhotoItem galleryItem)
                {
                    indices.Add(galleryItem.Index);
                }
            }
            return indices;
        }

        if (slides != null && slides.SelectedIndex >= 0)
        {
            return [slides.SelectedIndex];
        }

        return [];
    }

    private void UpdateSelectionUi()
    {
        if (txtGallerySelection == null || txtBtnDelete == null || txtBtnRotate == null || btnRefine == null) return;

        bool isGrid = rbViewGrid?.IsChecked == true;
        int selectedCount = isGrid && lstGallery?.SelectedItems != null ? lstGallery.SelectedItems.Count : (slides?.SelectedIndex >= 0 ? 1 : 0);
        int totalCount = lstGallery?.Items.Count ?? slides?.Items.Count ?? 0;

        if (isGrid && selectedCount > 1)
        {
            string selectedFormat = Avalonia.Application.Current?.FindResource("TxtSelectedCount")?.ToString() ?? "({0} of {1} selected)";
            txtGallerySelection.Text = string.Format(selectedFormat, selectedCount, totalCount);
            txtGallerySelection.IsVisible = true;

            string deleteCountFormat = Avalonia.Application.Current?.FindResource("BtnDeleteCount")?.ToString() ?? "Delete ({0})";
            txtBtnDelete.Text = string.Format(deleteCountFormat, selectedCount);

            string rotateCountFormat = Avalonia.Application.Current?.FindResource("BtnRotateCount")?.ToString() ?? "Rotate ({0})";
            txtBtnRotate.Text = string.Format(rotateCountFormat, selectedCount);

            btnRefine.IsEnabled = false;
            ToolTip.SetTip(btnRefine, Avalonia.Application.Current?.FindResource("TipRefineMultiDisabled"));
        }
        else
        {
            txtGallerySelection.IsVisible = false;
            txtBtnDelete.Text = Avalonia.Application.Current?.FindResource("BtnDelete")?.ToString() ?? "Delete";
            txtBtnRotate.Text = Avalonia.Application.Current?.FindResource("BtnRotate")?.ToString() ?? "Rotate";

            bool hasValidSelection = totalCount > 0 && selectedCount > 0;
            btnRefine.IsEnabled = hasValidSelection;
            ToolTip.SetTip(btnRefine, Avalonia.Application.Current?.FindResource("TipRefine"));
        }

        if (pnlBatchSelectionActions != null)
        {
            pnlBatchSelectionActions.IsVisible = isGrid;
        }
    }

    private void UpdatePhotoCounterLabel()
    {
        if (lblPhotoInfo == null || slides == null || ScanSessions.Count == 0 || currentIndex >= ScanSessions.Count)
        {
            if (lblPhotoInfo != null) lblPhotoInfo.Text = "";
            return;
        }

        var session = ScanSessions[currentIndex];
        int total = session.PhotoCount;
        if (total == 0)
        {
            lblPhotoInfo.Text = "";
            return;
        }

        int current = slides.SelectedIndex + 1;
        string format = Avalonia.Application.Current?.FindResource("PhotoCounter")?.ToString() ?? "PHOTO {0} OF {1}";
        lblPhotoInfo.Text = string.Format(format, current, total);
    }

    private void BtnPreviousCroppedImage_Click(object? sender, RoutedEventArgs e)
    {
        if (!isLoading && slides != null) slides.Previous();
    }

    private void BtnNextCroppedImage_Click(object? sender, RoutedEventArgs e)
    {
        if (!isLoading && slides != null) slides.Next();
    }

    private async void PerformUndo()
    {
        if (ScanSessions.Count == 0 || !undoHistory.CanUndo) return;

        var action = undoHistory.Undo(idx => idx >= 0 && idx < ScanSessions.Count ? ScanSessions[idx].Activate() : null);
        if (action != null)
        {
            if (action.ScanIndex >= 0 && action.ScanIndex < ScanSessions.Count)
            {
                ScanSessions[action.ScanIndex].IsModified = true;
            }

            if (action.ScanIndex != currentIndex && action.ScanIndex >= 0 && action.ScanIndex < ScanSessions.Count)
            {
                ScanSessions[currentIndex].Deactivate();
                currentIndex = action.ScanIndex;
                await LoadPhotosToGuiAsync();
            }
            else
            {
                var engine = ScanSessions[currentIndex].Activate();
                int selected = slides != null ? Math.Clamp(slides.SelectedIndex, 0, Math.Max(0, engine.DetectedPhotos.Count - 1)) : 0;
                LoadCroppedPhotosToSlider();
                if (slides != null && engine.DetectedPhotos.Count > 0)
                {
                    slides.SelectedIndex = selected;
                }
            }
            string undoFormat = Avalonia.Application.Current?.FindResource("MsgUndo")?.ToString() ?? "Undid {0}.";
            lblStatus.Text = string.Format(undoFormat, action.Description);
        }
    }

    private async void PerformRedo()
    {
        if (ScanSessions.Count == 0 || !undoHistory.CanRedo) return;

        var action = undoHistory.Redo(idx => idx >= 0 && idx < ScanSessions.Count ? ScanSessions[idx].Activate() : null);
        if (action != null)
        {
            if (action.ScanIndex >= 0 && action.ScanIndex < ScanSessions.Count)
            {
                ScanSessions[action.ScanIndex].IsModified = true;
            }

            if (action.ScanIndex != currentIndex && action.ScanIndex >= 0 && action.ScanIndex < ScanSessions.Count)
            {
                ScanSessions[currentIndex].Deactivate();
                currentIndex = action.ScanIndex;
                await LoadPhotosToGuiAsync();
            }
            else
            {
                var engine = ScanSessions[currentIndex].Activate();
                int selected = slides != null ? Math.Clamp(slides.SelectedIndex, 0, Math.Max(0, engine.DetectedPhotos.Count - 1)) : 0;
                LoadCroppedPhotosToSlider();
                if (slides != null && engine.DetectedPhotos.Count > 0)
                {
                    slides.SelectedIndex = selected;
                }
            }
            string redoFormat = Avalonia.Application.Current?.FindResource("MsgRedo")?.ToString() ?? "Redid {0}.";
            lblStatus.Text = string.Format(redoFormat, action.Description);
        }
    }

    private async Task RotateSelectedPhotosCcwAsync()
    {
        if (isLoading || ScanSessions.Count == 0) return;

        var selectedIndices = GetSelectedPhotoIndices();
        if (selectedIndices.Count == 0) return;

        var currentEngine = ScanSessions[currentIndex].Activate();
        var validIndices = selectedIndices.Distinct()
            .Where(idx => idx >= 0 && idx < currentEngine.DetectedPhotos.Count)
            .OrderBy(idx => idx)
            .ToList();

        if (validIndices.Count == 0) return;

        ScanSessions[currentIndex].IsModified = true;

        var actions = new List<IUndoableAction>();
        foreach (var idx in validIndices)
        {
            actions.Add(new RotatePhotoAction(currentIndex, idx));
        }

        string desc = validIndices.Count == 1 ? "Rotate 90° CCW" : $"Batch Rotate CCW ({validIndices.Count} photos)";
        undoHistory.PushBatch(currentIndex, actions, desc);

        string rotatingFormat = Avalonia.Application.Current?.FindResource("MsgBatchRotating")?.ToString() ?? "Rotating {0} photos...";
        string rotatedFormat = Avalonia.Application.Current?.FindResource("MsgBatchRotated")?.ToString() ?? "{0} photos rotated.";

        await ExecuteWithLoadingAsync(string.Format(rotatingFormat, validIndices.Count), async () =>
        {
            await Task.Run(() =>
            {
                foreach (var idx in validIndices)
                {
                    currentEngine.RotatePhotoCounterClockwise(idx);
                }
            });

            LoadCroppedPhotosToSlider();

            if (lstGallery?.SelectedItems != null)
            {
                isSyncingSelection = true;
                try
                {
                    lstGallery.SelectedItems.Clear();
                    foreach (var idx in validIndices)
                    {
                        if (idx < lstGallery.Items.Count)
                        {
                            lstGallery.SelectedItems.Add(lstGallery.Items[idx]);
                        }
                    }
                }
                finally
                {
                    isSyncingSelection = false;
                }
            }
            UpdateSelectionUi();
        }, string.Format(rotatedFormat, validIndices.Count));
    }

    private async void ContextMenu_RotateCw_Click(object? sender, RoutedEventArgs e)
    {
        EnsureContextSelection(sender);
        await RotateSelectedPhotosAsync();
    }

    private async void ContextMenu_RotateCcw_Click(object? sender, RoutedEventArgs e)
    {
        EnsureContextSelection(sender);
        await RotateSelectedPhotosCcwAsync();
    }

    private void ContextMenu_Refine_Click(object? sender, RoutedEventArgs e)
    {
        EnsureContextSelection(sender);
        StartRefineMode();
    }

    private void ContextMenu_Delete_Click(object? sender, RoutedEventArgs e)
    {
        EnsureContextSelection(sender);
        DeleteSelectedPhotos();
    }

    private async void ContextMenu_ExportSingle_Click(object? sender, RoutedEventArgs e)
    {
        EnsureContextSelection(sender);
        int selectedIndex = slides?.SelectedIndex ?? -1;
        if (selectedIndex >= 0)
        {
            await ExportSinglePhotoAsync(selectedIndex);
        }
    }

    private void EnsureContextSelection(object? sender)
    {
        if (sender is MenuItem menuItem)
        {
            if (menuItem.DataContext is GalleryPhotoItem item)
            {
                if (lstGallery != null && item.Index >= 0 && item.Index < lstGallery.Items.Count)
                {
                    if (!lstGallery.SelectedItems!.Contains(item))
                    {
                        lstGallery.SelectedItems.Clear();
                        lstGallery.SelectedItems.Add(item);
                        lstGallery.SelectedIndex = item.Index;
                    }
                }
                if (slides != null && item.Index >= 0 && item.Index < slides.Items.Count)
                {
                    slides.SelectedIndex = item.Index;
                }
            }
        }
    }
}
