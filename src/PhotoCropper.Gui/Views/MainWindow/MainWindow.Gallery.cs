using Avalonia.Controls;
using Avalonia.Interactivity;
using PhotoCropper.Gui.Services;
using PhotoCropper.Gui.ViewModels;

namespace PhotoCropper.Gui;

/// <summary>
/// The photo list of the Crops stage lives in <see cref="CropsStageViewModel"/>; these are the window's entry points
/// to it (menus, shortcuts, context menus) and undo/redo, which also touches the scan view.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The photo shown in the carousel, as an index into the open scan's photos (-1: none).</summary>
    private int SelectedPhotoNumber => _cropsVm.SelectedPhotoIndices() is [var only] ? only
        : _cropsVm.SelectedPhotoIndex >= 0 && _cropsVm.SelectedPhotoIndex < _cropsVm.Photos.Count ? _cropsVm.Photos[_cropsVm.SelectedPhotoIndex].Index : -1;

    private void BtnDelete_Click(object? sender, RoutedEventArgs e) => DeleteSelectedPhotos();

    private void DeleteSelectedPhotos()
    {
        if (isLoading) return;
        _cropsVm.DeleteSelectedPhotos();
    }

    private Task RotateSelectedPhotosAsync() => isLoading ? Task.CompletedTask : _cropsVm.RotateSelectedPhotosAsync();

    private Task RotateSelectedPhotosCcwAsync() => isLoading ? Task.CompletedTask : _cropsVm.RotateSelectedPhotosCounterClockwiseAsync();

    private void SelectAllGalleryPhotos() => _cropsVm.SelectAllCommand.Execute(null);

    private void ClearGallerySelection() => _cropsVm.ClearSelectionCommand.Execute(null);

    private void ApplyUndoRedoAction(IUndoableAction? action, string statusMessageKey, string statusDefaultFormat)
    {
        if (action == null) return;

        if (action.ScanIndex >= 0 && action.ScanIndex < ScanSessions.Count)
        {
            ScanSessions[action.ScanIndex].IsModified = true;
        }

        if (action.ScanIndex != CurrentIndex && action.ScanIndex >= 0 && action.ScanIndex < ScanSessions.Count)
        {
            // Navigate to the scan the action belongs to.
            // The undo/redo was already applied to that scan's engine by the engineAccessor callback
            // inside undoHistory.Undo/Redo. We must NOT call LoadPhotosToGuiAsync here because
            // it would re-activate the engine (DetectPhotos) and overwrite the undo state.
            ScanSessions[CurrentIndex].TryDeactivateIfUnmodified();
            _sessionManager.MoveTo(action.ScanIndex);

            // Mark the session as already processed so LoadPhotosToGuiAsync takes the fast path
            // (just refresh gallery) without re-running AutoTune or DetectPhotos.
            ScanSessions[action.ScanIndex].IsAutoTuned = true;

            var targetSession = ScanSessions[action.ScanIndex];
            var targetEngine = targetSession.Activate();
            _cropsVm.ShowOptions(targetEngine.CurrentOptions);
            ShowScan(targetEngine);
            _cropsVm.UpdateScanCounter();
            _cropsVm.ShowPhotos();
            _cropsVm.UpdateCoverage();
        }
        else
        {
            var engine = ScanSessions[CurrentIndex].Activate();
            // Refresh the main scan overlay (bounding boxes) as well as the photo list, keeping the shown photo.
            ShowScan(engine);
            _cropsVm.ShowPhotos(Math.Max(0, _cropsVm.SelectedPhotoIndex));
            _cropsVm.UpdateCoverage();
        }

        lblStatus.Text = LocalizationService.Format(statusMessageKey, statusDefaultFormat, action.Description);

        if (IsInReviewMode)
        {
            _reviewVm.Close();
            _ = OpenReviewModeAsync();
        }
    }

    private Task PerformUndoAsync()
    {
        if (isLoading || _isNavigating || ScanSessions.Count == 0 || !undoHistory.CanUndo) return Task.CompletedTask;

        var action = undoHistory.Undo(idx => idx >= 0 && idx < ScanSessions.Count ? ScanSessions[idx].Activate() : null);
        ApplyUndoRedoAction(action, ResourceKeys.MsgUndo, "Undid {0}.");
        return Task.CompletedTask;
    }

    private Task PerformRedoAsync()
    {
        if (isLoading || _isNavigating || ScanSessions.Count == 0 || !undoHistory.CanRedo) return Task.CompletedTask;

        var action = undoHistory.Redo(idx => idx >= 0 && idx < ScanSessions.Count ? ScanSessions[idx].Activate() : null);
        ApplyUndoRedoAction(action, ResourceKeys.MsgRedo, "Redid {0}.");
        return Task.CompletedTask;
    }
}
