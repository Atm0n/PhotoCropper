using Avalonia.Input;
using Avalonia.Interactivity;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Export;
using PhotoCropper.Core.IO;
using PhotoCropper.Core.Models;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;
using System.Diagnostics;

namespace PhotoCropper.Gui;

internal sealed partial class MainWindow
{
    private void Window_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void Window_Drop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer == null) return;

        var asyncTransfer = e.DataTransfer as IAsyncDataTransfer;
        var files = asyncTransfer != null ? await asyncTransfer.TryGetFilesAsync() : null;
        if (files == null) return;

        var rawPaths = files.Select(f => f.Path.LocalPath);
        var collected = ImageFileCollector.CollectFiles(rawPaths, recursive: true);
        if (collected.Count > 0)
        {
            await LoadScansFromPathsAsync(collected);
        }
    }

    private async Task LoadScansFromPathsAsync(IEnumerable<string> paths)
    {
        undoHistory.Clear();
        var options = _cropsVm.GetDetectionOptions();
        var settings = SettingsManager.Instance.Settings;
        var newSessions = new List<ScanSessionItem>();

        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            bool isAlreadyExported = ExportPathResolver.IsScanExported(
                path,
                settings.CustomOutputDirectory,
                settings.WorkDirectory);

            PhotoCropper.Core.Workspace.WorkspaceScanEntry? entry = null;
            if (_workspaceSession != null)
            {
                entry = _workspaceSession.Scans.FirstOrDefault(s =>
                    string.Equals(s.RelativePath, path, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(s.RelativePath), Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));
            }

            bool hasSavedCrops = entry != null && entry.FinalCrops.Count > 0;
            bool treatAsSaved = isAlreadyExported && hasSavedCrops;
            bool isModified = !treatAsSaved;

            newSessions.Add(new ScanSessionItem(path, options, isSaved: treatAsSaved, isModified: isModified, savedCrops: entry?.FinalCrops)
            {
                ExportedFiles = entry != null ? [.. entry.ExportedFiles] : []
            });
        }

        _sessionManager.ReplaceAll(newSessions);
        UpdateEmptyStateVisibility();
        await LoadPhotosToGuiAsync();
    }


    /// <summary>Opens the current scan in the Crops editor (see <see cref="CropsStageViewModel.LoadCurrentScanAsync"/>).</summary>
    private async Task LoadPhotosToGuiAsync()
    {
        UpdateEmptyStateVisibility();
        RefreshStages();
        if (ScanSessions.Count == 0 || isLoading) return;
        await _cropsVm.LoadCurrentScanAsync();
    }

    private void UpdateEmptyStateVisibility() => _cropsVm.UpdateScanCounter();

    private async void BtnDeleteScan_Click(object? sender, RoutedEventArgs e)
    {
        await DeleteCurrentScanAsync();
    }

    private Task DeleteCurrentScanAsync() => DeleteScanAtAsync(CurrentIndex);

    /// <summary>
    /// Deletes a scan from the session and from disk. In a project its photos are also removed from the pipeline
    /// (records, and Cropped/Final files that are unchanged).
    /// </summary>
    private async Task DeleteScanAtAsync(int index)
    {
        if (isLoading || _isNavigating || index < 0 || index >= ScanSessions.Count) return;

        string filePath = ScanSessions[index].FilePath;
        string fileName = Path.GetFileName(filePath);

        _sessionManager.RemoveAt(index);

        // Undo entries refer to scans by position, which just shifted.
        undoHistory.Clear();

        var settings = SettingsManager.Instance.Settings;
        if (!string.IsNullOrEmpty(settings.WorkDirectory) && Directory.Exists(settings.WorkDirectory))
        {
            if (_workspaceSession != null)
            {
                PhotoPipelineService.RemoveScanPhotos(settings.WorkDirectory, _workspaceSession,
                    Path.GetRelativePath(settings.WorkDirectory, filePath));
            }
            ProjectWorkspaceService.DeleteScan(settings.WorkDirectory, filePath, _workspaceSession);
        }
        else if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
            }
            catch (IOException)
            {
            }
        }

        string statusMsg = LocalizationService.Format(ResourceKeys.MsgScanDeleted, "Scan '{0}' deleted.", fileName);

        if (!_sessionManager.HasScans)
        {
            ShowScan(null);
            _cropsVm.ClearPhotos();
            lblStatus.Text = statusMsg;
            UpdateEmptyStateVisibility();
            RefreshStages();
        }
        else
        {
            await LoadPhotosToGuiAsync();
            lblStatus.Text = statusMsg;
        }
    }
}
