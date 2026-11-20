using Avalonia.Media.Imaging;
using Mat = OpenCvSharp.Mat;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;
using System.Collections.ObjectModel;

namespace PhotoCropper.Gui.ViewModels;

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
                row.MarkChanged += _ => UpdateMarks();
                Photos.Add(row);
                _ = LoadThumbnailAsync(row, photo.Cropped!.GetFullPath(workDir));
            }
        }

        // Keep the previewed photo selected across refreshes (e.g. after importing).
        SelectedPhoto = selectedId == null ? null : Photos.FirstOrDefault(p => p.Title == selectedId);
        _markAnchor = null;
        UpdateMarks();
        UpdateSummary();
    }

    // ---- Several photos at once: marked in the grid, then one click applies to all of them ----

    private FinishPhotoViewModel? _markAnchor;

    /// <summary>How many photos are marked.</summary>
    [ObservableProperty]
    public partial int MarkedCount { get; set; }

    public bool HasMarks => MarkedCount > 0;

    /// <summary>"3 selected", for the bulk actions bar.</summary>
    public string MarkedText => LocalizationService.Format(ResourceKeys.LblMarkedCount, "{0} selected", MarkedCount);

    partial void OnMarkedCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasMarks));
        OnPropertyChanged(nameof(MarkedText));
    }

    private List<FinishPhotoViewModel> Marked => [.. Photos.Where(p => p.IsMarked)];

    private void UpdateMarks()
    {
        MarkedCount = Photos.Count(p => p.IsMarked);
        RaiseMarkedToggles();
    }

    /// <summary>
    /// Ctrl+click marks or unmarks one photo; Shift+click marks every photo from the last one clicked to this one.
    /// </summary>
    public void ToggleMark(FinishPhotoViewModel row, bool range)
    {
        ArgumentNullException.ThrowIfNull(row);
        int to = Photos.IndexOf(row);
        if (to < 0) return;
        int from = range && _markAnchor != null ? Photos.IndexOf(_markAnchor) : -1;
        if (from >= 0)
        {
            for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++) Photos[i].IsMarked = true;
        }
        else
        {
            row.IsMarked = !row.IsMarked;
        }
        _markAnchor = row;
    }

    [RelayCommand]
    private void MarkAll()
    {
        foreach (var row in Photos) row.IsMarked = true;
    }

    [RelayCommand]
    private void ClearMarks()
    {
        foreach (var row in Photos) row.IsMarked = false;
        _markAnchor = null;
    }

    /// <summary>Applies a preset to every marked photo.</summary>
    [RelayCommand]
    private void ApplyPresetToMarked(string? name)
    {
        foreach (var row in Marked) row.ApplyPreset(name);
    }

    // The bulk bar's checkboxes: ticked when every marked photo has the option, empty when none has it, and a dash
    // when some have it. A click turns it on for all of them, or off when all of them already had it.

    /// <summary>Automatic restoration for the marked photos.</summary>
    public bool? MarkedRestoreColors
    {
        get => MarkedState(p => p.RestoreColors);
        set => ToggleForMarked(MarkedRestoreColors, (p, on) => p.RestoreColors = on);
    }

    /// <summary>Dust and scratch removal for the marked photos.</summary>
    public bool? MarkedRemoveDust
    {
        get => MarkedState(p => p.RemoveDust);
        set => ToggleForMarked(MarkedRemoveDust, (p, on) => p.RemoveDust = on);
    }

    /// <summary>White-border trimming for the marked photos.</summary>
    public bool? MarkedTrimWhiteBorders
    {
        get => MarkedState(p => p.TrimWhiteBorders);
        set => ToggleForMarked(MarkedTrimWhiteBorders, (p, on) => p.TrimWhiteBorders = on);
    }

    private bool? MarkedState(Func<FinishPhotoViewModel, bool> option)
    {
        var marked = Marked;
        if (marked.Count == 0) return false;
        int on = marked.Count(option);
        return on == marked.Count ? true : on == 0 ? false : null;
    }

    private void ToggleForMarked(bool? current, Action<FinishPhotoViewModel, bool> set)
    {
        // Decided from the current state, not the value the checkbox sends, so a click on a dash always turns it on.
        bool on = current != true;
        foreach (var row in Marked) set(row, on);
        RaiseMarkedToggles();
    }

    private void RaiseMarkedToggles()
    {
        OnPropertyChanged(nameof(MarkedRestoreColors));
        OnPropertyChanged(nameof(MarkedRemoveDust));
        OnPropertyChanged(nameof(MarkedTrimWhiteBorders));
    }

    /// <summary>Copies the open photo's settings to every marked photo.</summary>
    [RelayCommand]
    private void CopySettingsToMarked()
    {
        var settings = SelectedPhoto?.Photo.Restoration;
        if (settings == null) return;
        foreach (var row in Marked) row.ApplySettings(settings);
    }

    /// <summary>Exports the marked photos that are new or changed since their last export.</summary>
    [RelayCommand]
    private Task ExportMarked() => ExportRowsAsync([.. Marked.Where(p => p.IsStale)]);

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
        if (row.IsMarked) RaiseMarkedToggles();
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
            using Mat rendered = Core.Export.RestoreStage.RenderKeepingBorder(fullQuality ? full : small, request.Restoration, repairs, out var picture);
            if (picture is { } keep) ShadeOutside(rendered, keep);
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

    /// <summary>
    /// Darkens everything outside <paramref name="keep"/>: the border the final photo leaves out. The preview keeps
    /// the whole photo so it lines up with the original and repair strokes land where they are painted.
    /// </summary>
    private static void ShadeOutside(Mat image, OpenCvSharp.Rect keep)
    {
        using Mat shade = new(image.Size(), OpenCvSharp.MatType.CV_8UC1, OpenCvSharp.Scalar.All(255));
        OpenCvSharp.Cv2.Rectangle(shade, keep, OpenCvSharp.Scalar.All(0), -1);
        using Mat darker = new();
        image.ConvertTo(darker, -1, 0.35, 0);
        darker.CopyTo(image, shade);
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
    private Task Export() => ExportRowsAsync([.. Photos.Where(p => p.IsStale)]);

    private async Task ExportRowsAsync(List<FinishPhotoViewModel> stale)
    {
        var workspace = _host.Workspace;
        string? workDir = _host.WorkDirectory;
        if (workspace == null || workDir == null || stale.Count == 0) return;

        var options = _host.GetRenderOptions();
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

        string message = failures.Count == 0
            ? LocalizationService.Format(ResourceKeys.MsgFinalExported, "Exported {0} photos to Final.", done)
            : LocalizationService.Format(ResourceKeys.MsgExportFailedStatus, "Saved {0} photos; {1} could not be exported.", done, failures.Count);
        _host.ShowStatus(message);
        _host.NotifyExportCompleted(message, failures.Count == 0);
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
