using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;
using System.Collections.ObjectModel;

namespace PhotoCropper.Gui.ViewModels;

/// <summary>One scan in the Scan stage list.</summary>
internal sealed partial class ScanRowViewModel(int index, string fileName, string photosText, bool isFlagged, string filePath = "") : ObservableObject
{
    public int Index { get; } = index;
    public string FileName { get; } = fileName;
    public string FilePath { get; } = filePath;
    [ObservableProperty]
    public partial string PhotosText { get; set; } = photosText;

    /// <summary>True when the scan needs attention in the Crops stage (e.g. no photos found).</summary>
    [ObservableProperty]
    public partial bool IsFlagged { get; set; } = isFlagged;

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
        var previousPaths = Scans.Select(r => r.FilePath).ToList();
        string? previousSelection = SelectedScan?.FilePath;
        int previousIndex = SelectedScan?.Index ?? 0;
        _rebuildingList = true;
        Scans.Clear();
        var sessions = _host.Scans;
        for (int i = 0; i < sessions.Count; i++)
        {
            var scan = sessions[i];
            int? count = KnownPhotoCount(_host.Workspace, _host.WorkDirectory, scan);
            var row = new ScanRowViewModel(i, Path.GetFileName(scan.FilePath), PhotoCountText(count), count == 0, scan.FilePath);
            Scans.Add(row);
            _ = LoadThumbnailAsync(row);
        }

        // Free thumbnails of scans that are gone (discarded or replaced).
        lock (_thumbnails)
        {
            foreach (string stale in _thumbnails.Keys.Where(k => !sessions.Any(s => string.Equals(s.FilePath, k, StringComparison.OrdinalIgnoreCase))).ToList())
            {
                _thumbnails[stale].Bitmap.Dispose();
                _thumbnails.Remove(stale);
            }
        }

        IsEmpty = Scans.Count == 0;
        Summary = LocalizationService.Format(ResourceKeys.StageScanStatus, "{0} scans", Scans.Count);
        _rebuildingList = false;
        RestoreSelection(previousPaths, Scans.Any(r => previousPaths.Contains(r.FilePath)) ? previousSelection : null,
            Scans.Any(r => previousPaths.Contains(r.FilePath)) ? previousIndex : 0);
    }

    private async Task LoadThumbnailAsync(ScanRowViewModel row)
    {
        try
        {
            if (!File.Exists(row.FilePath)) return;
            DateTime writeTime = File.GetLastWriteTimeUtc(row.FilePath);
            lock (_thumbnails)
            {
                if (_thumbnails.TryGetValue(row.FilePath, out var cached) && cached.WriteTime == writeTime)
                {
                    row.Thumbnail = cached.Bitmap;
                    return;
                }
            }

            var bitmap = await Task.Run(() =>
            {
                using var stream = File.OpenRead(row.FilePath);
                return Bitmap.DecodeToWidth(stream, ThumbnailWidth);
            });

            // A load that outlives a rebuild may finish off the UI thread (always in tests): keep the cache consistent.
            lock (_thumbnails)
            {
                if (_thumbnails.TryGetValue(row.FilePath, out var replaced)) replaced.Bitmap.Dispose();
                _thumbnails[row.FilePath] = (writeTime, bitmap);
            }
            row.Thumbnail = bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            System.Diagnostics.Trace.TraceWarning($"[PhotoCropper] Could not load scan thumbnail '{row.FilePath}': {ex.Message}");
        }
    }

    private static string PhotoCountText(int? count) => count switch
    {
        null => LocalizationService.GetString(ResourceKeys.ScanStatusNotChecked, "Not checked yet"),
        0 => LocalizationService.GetString(ResourceKeys.ScanStatusNoPhotos, "No photos found"),
        _ => LocalizationService.Format(ResourceKeys.ScanStatusPhotos, "{0} photos", count.Value)
    };

    /// <summary>How many photos the scan has, or null when it hasn't been detected yet ("not checked").</summary>
    internal static int? KnownPhotoCount(WorkspaceSessionState? workspace, string? workDirectory, Core.Workspace.ScanSessionItem scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        if (scan.IsActive) return scan.PhotoCount;

        if (workspace != null && workDirectory != null)
        {
            string relative = Path.GetRelativePath(workDirectory, scan.FilePath);
            int saved = PhotoPipelineService.GetScanPhotos(workspace, relative).Count;
            if (saved > 0) return saved;
        }

        // Boxes saved as they were edited.
        if (scan.SavedCrops is { } crops) return crops.Count;
        return scan.PhotoCount > 0 ? scan.PhotoCount : null;
    }

    [RelayCommand]
    private Task Scan() => _host.ScanAsync();

    [RelayCommand]
    private Task ConfigureScanner() => _host.ConfigureScannerAsync();

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
