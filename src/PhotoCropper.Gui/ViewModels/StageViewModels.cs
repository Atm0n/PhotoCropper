using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;
using System.Collections.ObjectModel;

namespace PhotoCropper.Gui.ViewModels;

/// <summary>The three steps of the workflow, matching the project folders RawScans → Cropped → Final.</summary>
internal enum AppStage
{
    Scan,
    Crops,
    Finish
}

/// <summary>What the stage view-models need from the window, kept as an interface so they can be tested without UI.</summary>
internal interface IStageHost
{
    /// <summary>The open project folder, or null when no project is open.</summary>
    string? WorkDirectory { get; }

    /// <summary>The open project's session, or null when no project is open.</summary>
    WorkspaceSessionState? Workspace { get; }

    IReadOnlyList<Core.Workspace.ScanSessionItem> Scans { get; }

    Task ScanAsync();
    Task ImportScansAsync();
    void OpenScanInCrops(int scanIndex);

    /// <summary>Deletes a scan from the project and disk, with its photos.</summary>
    Task DiscardScanAsync(int scanIndex);

    /// <summary>Scans a new page to replace a scan, keeping its place in the list.</summary>
    Task RescanAsync(int scanIndex);

    /// <summary>Asks the user for photo files to import (e.g. photos cropped elsewhere).</summary>
    Task<IReadOnlyList<string>> PickPhotosToImportAsync();

    /// <summary>How final photos are written (format, quality, naming pattern, default metadata, output folder).</summary>
    FinalRenderOptions GetRenderOptions();

    /// <summary>Restoration given to photos that are new to the pipeline.</summary>
    RestorationSettings DefaultRestoration { get; }

    /// <summary>Runs work behind the loading overlay; returns false if it failed or was skipped.</summary>
    Task<bool> RunBusyAsync(string message, Func<Task> work);

    void SaveWorkspace();
    void ShowStatus(string message);
    void OpenFolder(string path);
}

/// <summary>One scan in the Scan stage list.</summary>
internal sealed partial class ScanRowViewModel(int index, string fileName, string photosText, bool isFlagged, string filePath = "") : ObservableObject
{
    public int Index { get; } = index;
    public string FileName { get; } = fileName;
    public string FilePath { get; } = filePath;
    public string PhotosText { get; } = photosText;

    /// <summary>True when the scan needs attention in the Crops stage (e.g. no photos found).</summary>
    public bool IsFlagged { get; } = isFlagged;

    [ObservableProperty]
    public partial Bitmap? Thumbnail { get; set; }

    /// <summary>True after a first click on Discard: the next click deletes the scan.</summary>
    [ObservableProperty]
    public partial bool IsConfirmingDiscard { get; set; }
}

/// <summary>Stage 1: acquire scans (scanner or import) and see how many photos each one has.</summary>
internal sealed partial class ScanStageViewModel : ObservableObject
{
    private readonly IStageHost _host;

    public ScanStageViewModel(IStageHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    public ObservableCollection<ScanRowViewModel> Scans { get; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; set; } = true;

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    /// <summary>Scan thumbnail width in pixels.</summary>
    internal const int ThumbnailWidth = 320;

    /// <summary>Decoded thumbnails by file path, reused while the file is unchanged (scans are large to decode).</summary>
    private readonly Dictionary<string, (DateTime WriteTime, Bitmap Bitmap)> _thumbnails = new(StringComparer.OrdinalIgnoreCase);

    public void Refresh()
    {
        Scans.Clear();
        var sessions = _host.Scans;
        for (int i = 0; i < sessions.Count; i++)
        {
            var scan = sessions[i];
            int? count = KnownPhotoCount(scan);
            string text = count switch
            {
                null => LocalizationService.GetString(ResourceKeys.ScanStatusNotChecked, "Not checked yet"),
                0 => LocalizationService.GetString(ResourceKeys.ScanStatusNoPhotos, "No photos found"),
                _ => LocalizationService.Format(ResourceKeys.ScanStatusPhotos, "{0} photos", count.Value)
            };
            var row = new ScanRowViewModel(i, Path.GetFileName(scan.FilePath), text, count == 0, scan.FilePath);
            Scans.Add(row);
            _ = LoadThumbnailAsync(row);
        }

        // Free thumbnails of scans that are gone (discarded or replaced).
        foreach (string stale in _thumbnails.Keys.Where(k => !sessions.Any(s => string.Equals(s.FilePath, k, StringComparison.OrdinalIgnoreCase))).ToList())
        {
            _thumbnails[stale].Bitmap.Dispose();
            _thumbnails.Remove(stale);
        }

        IsEmpty = Scans.Count == 0;
        Summary = LocalizationService.Format(ResourceKeys.StageScanStatus, "{0} scans", Scans.Count);
    }

    private async Task LoadThumbnailAsync(ScanRowViewModel row)
    {
        try
        {
            if (!File.Exists(row.FilePath)) return;
            DateTime writeTime = File.GetLastWriteTimeUtc(row.FilePath);
            if (_thumbnails.TryGetValue(row.FilePath, out var cached) && cached.WriteTime == writeTime)
            {
                row.Thumbnail = cached.Bitmap;
                return;
            }

            var bitmap = await Task.Run(() =>
            {
                using var stream = File.OpenRead(row.FilePath);
                return Bitmap.DecodeToWidth(stream, ThumbnailWidth);
            });

            if (_thumbnails.TryGetValue(row.FilePath, out var replaced)) replaced.Bitmap.Dispose();
            _thumbnails[row.FilePath] = (writeTime, bitmap);
            row.Thumbnail = bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            System.Diagnostics.Trace.TraceWarning($"[PhotoCropper] Could not load scan thumbnail '{row.FilePath}': {ex.Message}");
        }
    }

    /// <summary>Photos known for a scan: from its saved crops, or from detection if it has run; null if never checked.</summary>
    private int? KnownPhotoCount(Core.Workspace.ScanSessionItem scan)
    {
        var workspace = _host.Workspace;
        if (workspace != null && _host.WorkDirectory != null)
        {
            string relative = Path.GetRelativePath(_host.WorkDirectory, scan.FilePath);
            int saved = PhotoPipelineService.GetScanPhotos(workspace, relative).Count;
            if (saved > 0) return saved;
        }

        return scan.IsActive || scan.PhotoCount > 0 ? scan.PhotoCount : null;
    }

    [RelayCommand]
    private Task Scan() => _host.ScanAsync();

    [RelayCommand]
    private Task ImportScans() => _host.ImportScansAsync();

    [RelayCommand]
    private void OpenScan(ScanRowViewModel? row)
    {
        if (row != null) _host.OpenScanInCrops(row.Index);
    }

    /// <summary>
    /// Deletes a scan (file and photos). Takes two clicks: the first asks for confirmation on that card only,
    /// so a stray click can't delete a scan.
    /// </summary>
    [RelayCommand]
    private async Task Discard(ScanRowViewModel? row)
    {
        if (row == null) return;

        if (!row.IsConfirmingDiscard)
        {
            foreach (var other in Scans) other.IsConfirmingDiscard = false;
            row.IsConfirmingDiscard = true;
            return;
        }

        await _host.DiscardScanAsync(row.Index);
        Refresh();
    }

    /// <summary>Scans a new page to replace this scan, keeping its place in the list.</summary>
    [RelayCommand]
    private async Task Rescan(ScanRowViewModel? row)
    {
        if (row == null) return;
        foreach (var other in Scans) other.IsConfirmingDiscard = false;
        await _host.RescanAsync(row.Index);
        Refresh();
    }
}

/// <summary>One photo in the Finish stage: its cropped thumbnail, restoration choices and export status.</summary>
internal sealed partial class FinishPhotoViewModel : ObservableObject
{
    private readonly Action _changed;

    public FinishPhotoViewModel(ProjectPhoto photo, Action changed)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(changed);
        Photo = photo;
        _changed = changed;
    }

    public ProjectPhoto Photo { get; }

    public string Title => Photo.Id;

    public string SourceText => Photo.ScanRelativePath != null
        ? Path.GetFileName(Photo.ScanRelativePath)
        : LocalizationService.GetString(ResourceKeys.FinishSourceImported, "Imported");

    [ObservableProperty]
    public partial Bitmap? Thumbnail { get; set; }

    public bool RestoreColors
    {
        get => Photo.Restoration.RestoreColors;
        set
        {
            if (value == Photo.Restoration.RestoreColors) return;
            Photo.Restoration = Photo.Restoration with { RestoreColors = value };
            OnRestorationChanged();
        }
    }

    public bool RemoveDust
    {
        get => Photo.Restoration.RemoveDust;
        set
        {
            if (value == Photo.Restoration.RemoveDust) return;
            Photo.Restoration = Photo.Restoration with { RemoveDust = value };
            OnRestorationChanged();
        }
    }

    public bool IsStale => Photo.IsFinalStale;

    public string StatusText => Photo.Final == null
        ? LocalizationService.GetString(ResourceKeys.FinishStatusNew, "Not exported yet")
        : Photo.IsFinalStale
            ? LocalizationService.GetString(ResourceKeys.FinishStatusStale, "Changed since export")
            : LocalizationService.GetString(ResourceKeys.FinishStatusExported, "Exported");

    /// <summary>Re-reads the record after it was changed elsewhere (e.g. after exporting).</summary>
    public void NotifyRecordChanged()
    {
        OnPropertyChanged(nameof(RestoreColors));
        OnPropertyChanged(nameof(RemoveDust));
        OnPropertyChanged(nameof(IsStale));
        OnPropertyChanged(nameof(StatusText));
    }

    private void OnRestorationChanged()
    {
        NotifyRecordChanged();
        _changed();
    }
}

/// <summary>Stage 3: restore cropped photos (each with its own settings) and export them to Final/.</summary>
internal sealed partial class FinishStageViewModel : ObservableObject
{
    /// <summary>Thumbnail width in pixels; decoding at this size keeps memory low for large photos.</summary>
    internal const int ThumbnailWidth = 240;

    private readonly IStageHost _host;

    public FinishStageViewModel(IStageHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    public ObservableCollection<FinishPhotoViewModel> Photos { get; } = [];

    [ObservableProperty]
    public partial bool HasProject { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; } = true;

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    public void Refresh()
    {
        foreach (var old in Photos) old.Thumbnail?.Dispose();
        Photos.Clear();

        var workspace = _host.Workspace;
        string? workDir = _host.WorkDirectory;
        HasProject = workspace != null && workDir != null;
        if (workspace != null && workDir != null)
        {
            List<ProjectPhoto> photos;
            lock (workspace)
            {
                photos = [.. workspace.Photos.Where(p => p.Cropped != null)
                    .OrderBy(p => p.ScanRelativePath == null)
                    .ThenBy(p => p.ScanRelativePath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.Number)];
            }

            foreach (var photo in photos)
            {
                var row = new FinishPhotoViewModel(photo, OnPhotoChanged);
                Photos.Add(row);
                _ = LoadThumbnailAsync(row, photo.Cropped!.GetFullPath(workDir));
            }
        }

        UpdateSummary();
    }

    private static async Task LoadThumbnailAsync(FinishPhotoViewModel row, string path)
    {
        try
        {
            var bitmap = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                return Bitmap.DecodeToWidth(stream, ThumbnailWidth);
            });
            row.Thumbnail = bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            System.Diagnostics.Trace.TraceWarning($"[PhotoCropper] Could not load thumbnail '{path}': {ex.Message}");
        }
    }

    private void OnPhotoChanged()
    {
        // Per-photo choices are saved as they are made, not only on export.
        _host.SaveWorkspace();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        int stale = Photos.Count(p => p.IsStale);
        IsEmpty = Photos.Count == 0;
        Summary = LocalizationService.Format(ResourceKeys.StageFinishStatus, "{0} photos · {1} to export", Photos.Count, stale);
        ExportCommand.NotifyCanExecuteChanged();
    }

    private bool CanExport() => HasProject && Photos.Any(p => p.IsStale);

    /// <summary>Writes the final version of every photo that is new or changed since its last export.</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task Export()
    {
        var workspace = _host.Workspace;
        string? workDir = _host.WorkDirectory;
        if (workspace == null || workDir == null) return;

        var options = _host.GetRenderOptions();
        var stale = Photos.Where(p => p.IsStale).ToList();
        int done = 0;
        var failures = new List<string>();

        await _host.RunBusyAsync(
            LocalizationService.Format(ResourceKeys.MsgExportingFinal, "Exporting {0} photos...", stale.Count),
            () => Task.Run(() =>
            {
                foreach (var row in stale)
                {
                    try
                    {
                        PhotoPipelineService.RenderFinal(workDir, workspace, row.Photo, options);
                        done++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                    {
                        failures.Add($"{row.Title}: {ex.Message}");
                    }
                }
            }));

        _host.SaveWorkspace();
        foreach (var row in stale) row.NotifyRecordChanged();
        UpdateSummary();

        _host.ShowStatus(failures.Count == 0
            ? LocalizationService.Format(ResourceKeys.MsgFinalExported, "Exported {0} photos to Final.", done)
            : LocalizationService.Format(ResourceKeys.MsgExportFailedStatus, "Saved {0} photos; {1} scan(s) failed to export.", done, failures.Count));
    }

    /// <summary>Imports photos that were already cropped (e.g. elsewhere) so they can be restored here.</summary>
    [RelayCommand]
    private async Task ImportPhotos()
    {
        var workspace = _host.Workspace;
        string? workDir = _host.WorkDirectory;
        if (workspace == null || workDir == null) return;

        var paths = await _host.PickPhotosToImportAsync();
        if (paths.Count == 0) return;

        var restoration = _host.DefaultRestoration;
        await _host.RunBusyAsync(
            LocalizationService.Format(ResourceKeys.MsgImportingPhotos, "Importing {0} photos...", paths.Count),
            () => Task.Run(() =>
            {
                foreach (string path in paths)
                {
                    PhotoPipelineService.ImportPhoto(workDir, workspace, path, restoration);
                }
            }));

        _host.SaveWorkspace();
        Refresh();
    }

    [RelayCommand]
    private void RestoreAll() => SetRestoreColors(true);

    [RelayCommand]
    private void RestoreNone() => SetRestoreColors(false);

    private void SetRestoreColors(bool restore)
    {
        foreach (var row in Photos)
        {
            row.RestoreColors = restore;
        }
    }

    [RelayCommand]
    private void OpenFinalFolder()
    {
        if (_host.WorkDirectory != null)
        {
            _host.OpenFolder(ProjectWorkspaceService.GetFinalDirectory(_host.WorkDirectory));
        }
    }
}
