using Avalonia.Media.Imaging;
using Mat = OpenCvSharp.Mat;
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

    /// <summary>Opens the scanner settings (device, resolution, page size).</summary>
    Task ConfigureScannerAsync();

    /// <summary>Finds a scan's photos as the Crops stage does (it is then ready there) and returns their boxes.</summary>
    Task<IReadOnlyList<WorkspaceCropData>> PrepareScanAsync(int scanIndex, CancellationToken ct);

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

/// <summary>One photo in the Finish stage: its cropped thumbnail, restoration choices and export status.</summary>
internal sealed partial class FinishPhotoViewModel : ObservableObject
{
    private readonly Action<FinishPhotoViewModel> _changed;

    public FinishPhotoViewModel(ProjectPhoto photo, Action<FinishPhotoViewModel> changed)
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

    // ---- Restoration settings (each change updates the photo's record and is saved shortly after) ----

    public bool RestoreColors
    {
        get => Photo.Restoration.RestoreColors;
        set => Update(s => s with { RestoreColors = value });
    }

    public bool RemoveDust
    {
        get => Photo.Restoration.RemoveDust;
        set => Update(s => s with { RemoveDust = value });
    }

    /// <summary>0 = Gentle, 1 = Normal, 2 = Strong (bound to a ComboBox).</summary>
    public int StrengthIndex
    {
        get => (int)Photo.Restoration.Strength;
        set => Update(s => s with { Strength = (RestorationStrength)Math.Clamp(value, 0, 2) });
    }

    public bool AutoWhiteBalance
    {
        get => Photo.Restoration.AutoWhiteBalance;
        set => Update(s => s with { AutoWhiteBalance = value });
    }

    public bool AutoContrast
    {
        get => Photo.Restoration.AutoContrast;
        set => Update(s => s with { AutoContrast = value });
    }

    public bool AutoSaturation
    {
        get => Photo.Restoration.AutoSaturation;
        set => Update(s => s with { AutoSaturation = value });
    }

    /// <summary>0 = Auto, 1 = Colour, 2 = Black &amp; white (keep tone), 3 = Black &amp; white (neutral).</summary>
    public int ColorModeIndex
    {
        get => (int)Photo.Restoration.ColorMode;
        set => Update(s => s with { ColorMode = (PhotoColorMode)Math.Clamp(value, 0, 3) });
    }

    public double Brightness
    {
        get => Photo.Restoration.Brightness;
        set => Update(s => s with { Brightness = ToAdjustment(value) });
    }

    public double Contrast
    {
        get => Photo.Restoration.Contrast;
        set => Update(s => s with { Contrast = ToAdjustment(value) });
    }

    public double Warmth
    {
        get => Photo.Restoration.Warmth;
        set => Update(s => s with { Warmth = ToAdjustment(value) });
    }

    public double Tint
    {
        get => Photo.Restoration.Tint;
        set => Update(s => s with { Tint = ToAdjustment(value) });
    }

    public double Saturation
    {
        get => Photo.Restoration.Saturation;
        set => Update(s => s with { Saturation = ToAdjustment(value) });
    }

    private static int ToAdjustment(double value) => (int)Math.Round(Math.Clamp(value, -100, 100));

    /// <summary>Replaces all settings with a preset ("faded", "bw", "sepia", "dust", "none").</summary>
    [RelayCommand]
    private void ApplyPreset(string? name)
    {
        var preset = RestorationSettings.FromPresetName(name);
        if (preset != null) ApplySettings(preset);
    }

    /// <summary>Sets the manual adjustments back to 0, keeping the automatic settings.</summary>
    [RelayCommand]
    private void ResetAdjustments() =>
        Update(s => s with { Brightness = 0, Contrast = 0, Warmth = 0, Tint = 0, Saturation = 0 });

    /// <summary>Replaces this photo's settings (e.g. copied from another photo).</summary>
    public void ApplySettings(RestorationSettings settings) => Update(_ => settings);

    // ---- Repair brush ----

    public int RepairCount => Photo.Repairs.Count;

    public bool HasRepairs => Photo.Repairs.Count > 0;

    /// <summary>
    /// Adds a repair-brush stroke painted on the cropped photo of <paramref name="imageWidth"/> × <paramref name="imageHeight"/>.
    /// Strokes left from an earlier crop of a different size couldn't be applied anyway, so they are replaced.
    /// </summary>
    public void AddRepair(RepairStroke stroke, int imageWidth, int imageHeight)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        if (stroke.PointCount == 0) return;

        if (Photo.AreRepairsOutdated(imageWidth, imageHeight)) Photo.Repairs.Clear();
        Photo.RepairImageWidth = imageWidth;
        Photo.RepairImageHeight = imageHeight;
        Photo.Repairs.Add(stroke);
        OnRestorationChanged();
    }

    [RelayCommand]
    private void UndoRepair()
    {
        if (Photo.Repairs.Count == 0) return;
        Photo.Repairs.RemoveAt(Photo.Repairs.Count - 1);
        OnRestorationChanged();
    }

    [RelayCommand]
    private void ClearRepairs()
    {
        if (Photo.Repairs.Count == 0) return;
        Photo.Repairs.Clear();
        OnRestorationChanged();
    }

    private void Update(Func<RestorationSettings, RestorationSettings> change)
    {
        var updated = change(Photo.Restoration);
        if (updated == Photo.Restoration) return; // records compare by value: nothing changed
        Photo.Restoration = updated;
        OnRestorationChanged();
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
        // Every setting is read from the record, so refresh them all.
        OnPropertyChanged(string.Empty);
    }

    private void OnRestorationChanged()
    {
        NotifyRecordChanged();
        _changed(this);
    }
}

/// <summary>Stage 3: restore cropped photos (each with its own settings) and export them to Final/.</summary>
internal sealed partial class FinishStageViewModel : ObservableObject, IDisposable
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
        string? selectedId = SelectedPhoto?.Title;
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

        // Keep the previewed photo selected across refreshes (e.g. after importing).
        SelectedPhoto = selectedId == null ? null : Photos.FirstOrDefault(p => p.Title == selectedId);
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

    /// <summary>
    /// How long after the last setting change the workspace is saved and the preview re-rendered, so dragging a
    /// slider doesn't write session.json and re-render on every step. Zero commits immediately (used by tests).
    /// </summary>
    internal TimeSpan ChangeDebounce { get; set; } = TimeSpan.FromMilliseconds(250);

    private CancellationTokenSource? _changeCts;

    private void OnPhotoChanged(FinishPhotoViewModel row)
    {
        UpdateSummary();
        CopySettingsToAllCommand.NotifyCanExecuteChanged();

        _changeCts?.Cancel();
        _changeCts?.Dispose();
        _changeCts = null;

        if (ChangeDebounce <= TimeSpan.Zero)
        {
            CommitChanges();
            return;
        }

        _changeCts = new CancellationTokenSource();
        _ = CommitAfterDelayAsync(_changeCts.Token);
    }

    private async Task CommitAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(ChangeDebounce, token);
        }
        catch (OperationCanceledException)
        {
            return; // superseded by a newer change
        }
        CommitChanges();
    }

    /// <summary>Saves per-photo choices (as they are made, not only on export) and refreshes the preview.</summary>
    private void CommitChanges()
    {
        _host.SaveWorkspace();
        if (SelectedPhoto != null) _ = UpdatePreviewAsync();
    }

    // ---- Before/after preview of the selected photo ----

    /// <summary>
    /// Longest side of the preview images. Large enough to judge detail when zoomed in the editor (most scanned
    /// photos are smaller, so they are shown at full size), small enough that changes still feel quick.
    /// </summary>
    internal const int PreviewMaxSize = 2048;

    private int _previewVersion;

    [ObservableProperty]
    public partial FinishPhotoViewModel? SelectedPhoto { get; set; }

    /// <summary>The cropped photo, as it is before restoration.</summary>
    [ObservableProperty]
    public partial Bitmap? BeforePreview { get; set; }

    /// <summary>The same photo with its current restoration settings, as Export would write it.</summary>
    [ObservableProperty]
    public partial Bitmap? AfterPreview { get; set; }

    [ObservableProperty]
    public partial bool IsPreviewBusy { get; set; }

    public bool HasSelection => SelectedPhoto != null;

    partial void OnSelectedPhotoChanged(FinishPhotoViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        CopySettingsToAllCommand.NotifyCanExecuteChanged();
        _ = UpdatePreviewAsync();
    }

    /// <summary>
    /// After a pause, the preview is rendered again from the full-size photo when dust removal is on: dust
    /// detection depends on pixel size, so only a full-size pass shows exactly what Export will write.
    /// </summary>
    internal TimeSpan FullQualityDelay { get; set; } = TimeSpan.FromMilliseconds(600);

    [RelayCommand]
    private void ClosePreview() => SelectedPhoto = null;

    /// <summary>Opens the next photo in the editor (wraps around).</summary>
    [RelayCommand]
    private void NextPhoto() => StepPhoto(+1);

    /// <summary>Opens the previous photo in the editor (wraps around).</summary>
    [RelayCommand]
    private void PreviousPhoto() => StepPhoto(-1);

    private void StepPhoto(int step)
    {
        if (SelectedPhoto == null || Photos.Count == 0) return;
        int index = Photos.IndexOf(SelectedPhoto);
        SelectedPhoto = Photos[((index + step) % Photos.Count + Photos.Count) % Photos.Count];
    }

    // ---- Repair brush (painting happens in the view; finished strokes arrive here) ----

    /// <summary>Brush mode: the preview shows one large "After" image to paint repairs on.</summary>
    [ObservableProperty]
    public partial bool IsRepairMode { get; set; }

    /// <summary>Brush size from 1 to 50; the brush radius is this many thousandths of the photo's width.</summary>
    [ObservableProperty]
    public partial double BrushSize { get; set; } = 8;

    /// <summary>The selected photo has strokes from an earlier crop of a different size; they are not applied.</summary>
    [ObservableProperty]
    public partial bool RepairsOutdated { get; set; }

    /// <summary>Full size of the selected cropped photo, known once its preview has been rendered.</summary>
    private System.Drawing.Size _previewSourceSize;

    internal float BrushRadius => (float)(Math.Clamp(BrushSize, 1, 50) / 1000.0);

    /// <summary>
    /// Adds a stroke painted on the preview. <paramref name="normalizedPoints"/> are fractions of the photo's width
    /// and height (0..1), as the view measures them on the displayed image.
    /// </summary>
    public void AddRepairStroke(IReadOnlyList<(double X, double Y)> normalizedPoints)
    {
        ArgumentNullException.ThrowIfNull(normalizedPoints);
        var row = SelectedPhoto;
        if (row == null || normalizedPoints.Count == 0 || _previewSourceSize.IsEmpty) return;

        var stroke = new RepairStroke
        {
            Radius = BrushRadius,
            Method = UseAiFill && IsAiModelInstalled ? RepairMethod.Ai : RepairMethod.Quick,
        };
        foreach (var (x, y) in normalizedPoints)
        {
            stroke.Points.Add((float)Math.Clamp(x, 0, 1));
            stroke.Points.Add((float)Math.Clamp(y, 0, 1));
        }
        row.AddRepair(stroke, _previewSourceSize.Width, _previewSourceSize.Height);
    }

    [RelayCommand]
    private void ToggleRepairMode() => IsRepairMode = !IsRepairMode;

    // ---- AI fill (LaMa): optional model, downloaded once on request ----

    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromMinutes(30) };
    private CancellationTokenSource? _downloadCts;

    /// <summary>New strokes are filled by AI instead of the quick fill.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAiDownload))]
    public partial bool UseAiFill { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAiDownload))]
    public partial bool IsAiModelInstalled { get; set; } = Core.Extraction.AiInpainting.IsAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAiDownload))]
    [NotifyCanExecuteChangedFor(nameof(DownloadAiModelCommand))]
    public partial bool IsAiModelDownloading { get; set; }

    /// <summary>Download progress, 0 to 100.</summary>
    [ObservableProperty]
    public partial double AiDownloadProgress { get; set; }

    /// <summary>Why the last download failed, or null.</summary>
    [ObservableProperty]
    public partial string? AiDownloadError { get; set; }

    /// <summary>The model is being loaded into memory (takes several seconds, once per session).</summary>
    [ObservableProperty]
    public partial bool IsAiModelLoading { get; set; }

    /// <summary>AI fill is chosen but the model isn't downloaded yet: offer the download.</summary>
    public bool ShowAiDownload => UseAiFill && !IsAiModelInstalled;

    /// <summary>Downloads and verifies the model (tests replace this).</summary>
    internal Func<IProgress<double>, CancellationToken, Task> DownloadModel { get; set; } =
        (progress, token) => Core.Extraction.AiModelStore.DownloadLamaAsync(SharedHttpClient, progress, token);

    /// <summary>Whether the model is downloaded (tests replace this).</summary>
    internal Func<bool> CheckAiModelInstalled { get; set; } = () => Core.Extraction.AiInpainting.IsAvailable;

    /// <summary>Loads the model in the background (tests replace this).</summary>
    internal Func<Task> PreloadModel { get; set; } = Core.Extraction.AiInpainting.PreloadAsync;

    partial void OnUseAiFillChanged(bool value)
    {
        if (value && IsAiModelInstalled) _ = PreloadAsync();
    }

    private async Task PreloadAsync()
    {
        if (Core.Extraction.AiInpainting.IsLoaded) return;
        IsAiModelLoading = true;
        try
        {
            await PreloadModel();
        }
        finally
        {
            IsAiModelLoading = false;
        }
    }

    private bool CanDownloadAiModel() => !IsAiModelDownloading;

    [RelayCommand(CanExecute = nameof(CanDownloadAiModel))]
    private async Task DownloadAiModelAsync()
    {
        _downloadCts?.Dispose();
        _downloadCts = new CancellationTokenSource();
        IsAiModelDownloading = true;
        AiDownloadError = null;
        AiDownloadProgress = 0;
        try
        {
            // Progress<T> reports on the UI thread it was created on.
            await DownloadModel(new Progress<double>(p => AiDownloadProgress = p * 100), _downloadCts.Token);
            IsAiModelInstalled = CheckAiModelInstalled();
            if (IsAiModelInstalled && UseAiFill) _ = PreloadAsync();
        }
        catch (OperationCanceledException)
        {
            AiDownloadProgress = 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            AiDownloadError = ex.Message;
        }
        finally
        {
            IsAiModelDownloading = false;
        }
    }

    [RelayCommand]
    private void CancelAiDownload() => _downloadCts?.Cancel();

    /// <summary>
    /// Renders the selected photo before and after its repairs and restoration (at preview size). Only the latest
    /// request is shown: a request superseded by a newer selection or change is discarded.
    /// </summary>
    internal async Task UpdatePreviewAsync()
    {
        int version = ++_previewVersion;
        var row = SelectedPhoto;
        string? workDir = _host.WorkDirectory;

        if (row?.Photo.Cropped == null || workDir == null)
        {
            SetPreview(null, null);
            IsPreviewBusy = false;
            RepairsOutdated = false;
            return;
        }

        // Snapshot the inputs: the user may keep painting or changing settings while this renders.
        var request = new PreviewRequest(
            row.Photo.Cropped.GetFullPath(workDir),
            row.Photo.Restoration,
            [.. row.Photo.Repairs],
            new System.Drawing.Size(row.Photo.RepairImageWidth, row.Photo.RepairImageHeight));
        IsPreviewBusy = true;

        var result = await TryRenderAsync(request, fullQuality: false);
        if (version != _previewVersion)
        {
            result?.Dispose(); // a newer request is on its way
            return;
        }

        if (result != null)
        {
            _previewSourceSize = result.SourceSize;
            RepairsOutdated = row.Photo.AreRepairsOutdated(result.SourceSize.Width, result.SourceSize.Height);
        }
        SetPreview(result?.Before, result?.After);
        IsPreviewBusy = false;

        // Dust removal and repairs fill in pixels, which looks slightly different at preview size than in the
        // export; after a pause, render them from the full photo so the preview matches exactly.
        bool needsExactPass = request.Restoration.RemoveDust || (request.Repairs.Count > 0 && !RepairsOutdated);
        if (result == null || !needsExactPass) return;
        if (FullQualityDelay > TimeSpan.Zero)
        {
            await Task.Delay(FullQualityDelay, CancellationToken.None);
            if (version != _previewVersion) return;
        }

        var exact = await TryRenderAsync(request, fullQuality: true);
        if (version != _previewVersion || exact == null)
        {
            exact?.Dispose();
            return;
        }
        SetPreview(exact.Before, exact.After);
    }

    private sealed record PreviewRequest(string Path, RestorationSettings Restoration, List<RepairStroke> Repairs, System.Drawing.Size RepairImageSize);

    private sealed record PreviewResult(Bitmap Before, Bitmap? After, System.Drawing.Size SourceSize) : IDisposable
    {
        public void Dispose()
        {
            if (After != null && !ReferenceEquals(After, Before)) After.Dispose();
            Before.Dispose();
        }
    }

    private static async Task<PreviewResult?> TryRenderAsync(PreviewRequest request, bool fullQuality)
    {
        try
        {
            return await Task.Run(() => RenderPreview(request, fullQuality));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            System.Diagnostics.Trace.TraceWarning($"[PhotoCropper] Could not render preview for '{request.Path}': {ex.Message}");
            return null;
        }
    }

    /// <param name="fullQuality">
    /// Repair and restore the full-size photo and shrink the result (exactly what Export writes), instead of working
    /// on a shrunken copy (fast, but filled-in areas can differ slightly).
    /// </param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The bitmaps are returned to the caller, which owns and disposes them; 'before' is disposed if rendering fails")]
    private static PreviewResult RenderPreview(PreviewRequest request, bool fullQuality)
    {
        using Mat full = Core.Export.RestoreStage.ReadImage(request.Path);
        using Mat small = Shrink(full);
        var sourceSize = new System.Drawing.Size(full.Width, full.Height);

        // Strokes from an earlier crop of a different size would land in the wrong place: skip them.
        List<RepairStroke> repairs = request.Repairs.Count > 0 && request.RepairImageSize == sourceSize
            ? request.Repairs
            : [];

        Bitmap before = MatBitmapConverter.ToAvaloniaBitmap(small);
        if (!request.Restoration.ChangesPhoto && repairs.Count == 0) return new PreviewResult(before, null, sourceSize);

        try
        {
            using Mat rendered = Core.Export.RestoreStage.Render(fullQuality ? full : small, request.Restoration, repairs);
            if (!fullQuality) return new PreviewResult(before, MatBitmapConverter.ToAvaloniaBitmap(rendered), sourceSize);

            using Mat shrunk = Shrink(rendered);
            return new PreviewResult(before, MatBitmapConverter.ToAvaloniaBitmap(shrunk), sourceSize);
        }
        catch
        {
            before.Dispose();
            throw;
        }
    }

    /// <summary>A copy no larger than <see cref="PreviewMaxSize"/> on its longest side.</summary>
    private static Mat Shrink(Mat image)
    {
        Mat result = new();
        double scale = Math.Min(1.0, (double)PreviewMaxSize / Math.Max(image.Width, image.Height));
        if (scale < 1.0)
        {
            OpenCvSharp.Cv2.Resize(image, result, new OpenCvSharp.Size(
                Math.Max(1, (int)Math.Round(image.Width * scale)),
                Math.Max(1, (int)Math.Round(image.Height * scale))), 0, 0, OpenCvSharp.InterpolationFlags.Area);
        }
        else
        {
            image.CopyTo(result);
        }
        return result;
    }

    public void Dispose()
    {
        _changeCts?.Cancel();
        _changeCts?.Dispose();
        _changeCts = null;
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        _downloadCts = null;
        _previewVersion++; // drop any preview still rendering
        SetPreview(null, null);
        foreach (var row in Photos) row.Thumbnail?.Dispose();
    }

    /// <summary>Shows new preview images; with no restoration, After shows the same image as Before.</summary>
    private void SetPreview(Bitmap? before, Bitmap? after)
    {
        var oldBefore = BeforePreview;
        var oldAfter = AfterPreview;
        BeforePreview = before;
        AfterPreview = after ?? before;
        if (oldAfter != null && !ReferenceEquals(oldAfter, oldBefore)) oldAfter.Dispose();
        oldBefore?.Dispose();
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
            }, CancellationToken.None));

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
            }, CancellationToken.None));

        _host.SaveWorkspace();
        Refresh();
    }

    /// <summary>Copies the selected photo's restoration settings (e.g. a tweaked preset) to every photo.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CopySettingsToAll()
    {
        var settings = SelectedPhoto?.Photo.Restoration;
        if (settings == null) return;
        foreach (var row in Photos)
        {
            row.ApplySettings(settings);
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
