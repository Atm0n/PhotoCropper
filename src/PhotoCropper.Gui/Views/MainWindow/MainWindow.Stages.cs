using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PhotoCropper.Core.Export;
using PhotoCropper.Core.IO;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;
using PhotoCropper.Gui.ViewModels;
using System.Diagnostics;

namespace PhotoCropper.Gui;

/// <summary>
/// The three-stage workflow: 1 Scan (RawScans) → 2 Crops (Cropped) → 3 Finish (Final). The Crops stage is the
/// existing scan editor; Scan and Finish are MVVM views whose view-models reach the window through IStageHost.
/// </summary>
internal sealed partial class MainWindow : IStageHost, ICropsStageHost, IRefineHost, Views.Stages.ICropsViewActions
{
    private AppStage _stage = AppStage.Crops;
    private ScanStageViewModel? _scanStageVm;
    private FinishStageViewModel? _finishStageVm;

    private void InitializeStages()
    {
        _scanStageVm = new ScanStageViewModel(this);
        _finishStageVm = new FinishStageViewModel(this);
        viewScanStage.DataContext = _scanStageVm;
        viewFinishStage.DataContext = _finishStageVm;
        ApplyStage(AppStage.Scan);
    }

    private async void BtnStageScan_Click(object? sender, RoutedEventArgs e) => await SetStageAsync(AppStage.Scan);
    private async void BtnStageCrops_Click(object? sender, RoutedEventArgs e) => await SetStageAsync(AppStage.Crops);
    private async void BtnStageFinish_Click(object? sender, RoutedEventArgs e) => await SetStageAsync(AppStage.Finish);

    /// <summary>
    /// Switches stage. Entering Finish first saves the crops of scans that may have changed into Cropped/, since
    /// Finish works only from those files.
    /// </summary>
    private async Task SetStageAsync(AppStage target)
    {
        if (isLoading) return;

        if (target == AppStage.Finish && !await SaveCropsAsync())
        {
            return;
        }

        ApplyStage(target);

        if (target == AppStage.Crops && ScanSessions.Count > 0)
        {
            await LoadPhotosToGuiAsync();
        }
    }

    private void ApplyStage(AppStage stage)
    {
        _stage = stage;
        viewScanStage.IsVisible = stage == AppStage.Scan;
        viewCrops.IsVisible = stage == AppStage.Crops;
        viewFinishStage.IsVisible = stage == AppStage.Finish;

        if (stage == AppStage.Scan) _scanStageVm?.Refresh();
        if (stage == AppStage.Finish) _finishStageVm?.Refresh();
        UpdateStageBar();
    }

    /// <summary>Refreshes the stage bar and the visible stage after scans were added, removed or detected.</summary>
    private void RefreshStages()
    {
        if (_stage == AppStage.Scan) _scanStageVm?.Refresh();
        UpdateStageBar();
    }

    private void UpdateStageBar()
    {
        StyleStageButton(btnStageScan, _stage == AppStage.Scan);
        StyleStageButton(btnStageCrops, _stage == AppStage.Crops);
        StyleStageButton(btnStageFinish, _stage == AppStage.Finish);

        lblStageScanStatus.Text = LocalizationService.Format(ResourceKeys.StageScanStatus, "{0} scans", ScanSessions.Count);

        var workspace = Workspace;
        string? workDir = WorkDirectory;
        if (workDir != null)
        {
            // A project without scans has no session yet: it simply has nothing saved.
            int scansChecked;
            int photos = 0;
            int toExport = 0;
            if (workspace != null)
            {
                lock (workspace)
                {
                    scansChecked = ScanSessions.Count(s => ScanStageViewModel.KnownPhotoCount(workspace, workDir, s) != null);
                    photos = workspace.Photos.Count(p => p.Cropped != null);
                    toExport = workspace.Photos.Count(p => p.Cropped != null && p.IsFinalStale);
                }
            }
            else
            {
                scansChecked = ScanSessions.Count(s => ScanStageViewModel.KnownPhotoCount(null, null, s) != null);
            }
            lblStageCropsStatus.Text = LocalizationService.Format(ResourceKeys.StageCropsStatus, "{0} of {1} scans checked", scansChecked, ScanSessions.Count);
            lblStageFinishStatus.Text = LocalizationService.Format(ResourceKeys.StageFinishStatus, "{0} photos · {1} to export", photos, toExport);
        }
        else
        {
            lblStageCropsStatus.Text = string.Empty;
            lblStageFinishStatus.Text = LocalizationService.GetString(ResourceKeys.StageNeedsProject, "Needs a project");
        }
    }

    // Colours come from the "stageStep" / "stageStep.active" styles in MainWindow.axaml, so they follow the theme.
    private static void StyleStageButton(Button button, bool isActive) => button.Classes.Set("active", isActive);

    /// <summary>
    /// Writes Cropped/ for every project scan that may have changed: scans whose editor is open (edits happen
    /// there) and scans that have no saved crops yet (they are detected now). Unchanged photos aren't rewritten.
    /// </summary>
    /// <returns>False if saving failed or was cancelled; the stage then stays where it is.</returns>
    private async Task<bool> SaveCropsAsync()
    {
        var workspace = Workspace;
        string? workDir = WorkDirectory;
        if (workspace == null || workDir == null) return true;

        var lookup = workspace.Scans
            .GroupBy(s => Path.GetFileName(s.RelativePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var pending = ScanSessions
            .Where(s => lookup.ContainsKey(Path.GetFileName(s.FilePath)))
            .Where(s => s.IsActive || PhotoPipelineService.GetScanPhotos(workspace, lookup[Path.GetFileName(s.FilePath)].RelativePath).Count == 0)
            .ToList();
        if (pending.Count == 0) return true;

        var restoration = DefaultRestoration;
        int done = 0;
        var failures = new System.Collections.Concurrent.ConcurrentQueue<(string FilePath, string Error)>();

        bool completed = await ExecuteWithLoadingAsync(
            LocalizationService.Format(ResourceKeys.MsgSavingCrops, "Saving crops ({0} of {1})...", 0, pending.Count),
            () => Task.Run(() =>
            {
                Parallel.ForEach(pending, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4) }, session =>
                {
                    try
                    {
                        bool wasActive = session.IsActive;
                        var engine = session.Activate();
                        try
                        {
                            var entry = lookup[Path.GetFileName(session.FilePath)];
                            var crops = BuildCropData(engine);
                            int count = Math.Min(engine.RawDetectedPhotos.Count, crops.Count);
                            var inputs = Enumerable.Range(0, count)
                                .Select(i => new CroppedPhotoInput(engine.RawDetectedPhotos[i], crops[i]))
                                .ToList();

                            PhotoPipelineService.SaveCroppedPhotos(workDir, workspace, entry, inputs, restoration);

                            entry.FinalCrops.Clear();
                            foreach (var crop in crops) entry.FinalCrops.Add(crop);
                        }
                        finally
                        {
                            if (!wasActive) session.Deactivate();
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceError($"[PhotoCropper] Failed to save crops for '{session.FilePath}': {ex.Message}");
                        failures.Enqueue((session.FilePath, ex.Message));
                    }

                    int finished = Interlocked.Increment(ref done);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => txtLoadingText.Text =
                        LocalizationService.Format(ResourceKeys.MsgSavingCrops, "Saving crops ({0} of {1})...", finished, pending.Count));
                });

                ProjectWorkspaceService.SaveSession(workDir, workspace);
            }));

        if (completed)
        {
            // Otherwise the status bar keeps the "Saving crops (0 of N)..." text the operation started with.
            lblStatus.Text = LocalizationService.Format(ResourceKeys.MsgCropsSaved, "Crops saved for {0} scans.", pending.Count - failures.Count);
        }

        if (!failures.IsEmpty)
        {
            ShowAppError(
                LocalizationService.GetString(ResourceKeys.TitleExportFailed, "Some Scans Could Not Be Exported"),
                FormatFailedScanList([.. failures]));
        }

        return completed;
    }

    // ---- ICropsStageHost ----

    bool ICropsStageHost.IsBusy => isLoading;

    Task<bool> ICropsStageHost.RunBusyAsync(string message, Func<CancellationToken, Task> work) => ExecuteWithLoadingAsync(message, work);

    void ICropsStageHost.ShowScanResult(PhotoCropper.Core.PhotoCropperEngine engine)
    {
        ShowScan(engine);
    }

    void ICropsStageHost.ShowBoxes(PhotoCropper.Core.PhotoCropperEngine engine) => ShowScan(engine);

    void ICropsStageHost.DisposeLater(IReadOnlyCollection<IDisposable> items) => DeferDispose(items);

    // ---- ICropsViewActions (buttons and menus inside the Crops view) ----

    void Views.Stages.ICropsViewActions.StartRefine() => StartRefineMode();

    Task Views.Stages.ICropsViewActions.OpenReviewAsync() => OpenReviewModeAsync();

    Task Views.Stages.ICropsViewActions.ExportSinglePhotoAsync(int photoIndex) => ExportSinglePhotoAsync(photoIndex, CancellationToken.None);

    Task Views.Stages.ICropsViewActions.DeleteCurrentScanAsync() => DeleteCurrentScanAsync();

    // ---- IRefineHost ----

    private RefineViewModel _refineVm = null!;

    Task<bool> IRefineHost.RunBusyAsync(string message, Func<CancellationToken, Task> work) => ExecuteWithLoadingAsync(message, work);

    void IRefineHost.DisposeLater(IReadOnlyCollection<IDisposable> items) => DeferDispose(items);

    void IRefineHost.Refined(PhotoCropper.Core.PhotoCropperEngine engine, int photoIndex)
    {
        ShowScan(engine);
        _cropsVm.ShowPhotos(photoIndex);
        _cropsVm.UpdateCoverage();
    }

    // ---- IStageHost ----

    public string? WorkDirectory
    {
        get
        {
            string? dir = SettingsManager.Instance.Settings.WorkDirectory;
            return _workspaceSession != null && !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : null;
        }
    }

    public WorkspaceSessionState? Workspace => WorkDirectory != null ? _workspaceSession : null;

    IReadOnlyList<ScanSessionItem> IStageHost.Scans => ScanSessions;

    public async Task ScanAsync()
    {
        await ScanNewAsync();
        RefreshStages();
    }

    public Task ConfigureScannerAsync() => ShowScannerConfigAsync();

    public Task<IReadOnlyList<WorkspaceCropData>> PrepareScanAsync(int scanIndex, CancellationToken ct) =>
        scanIndex >= 0 && scanIndex < ScanSessions.Count
            ? _cropsVm.PrepareScanAsync(ScanSessions[scanIndex], ct)
            : Task.FromResult<IReadOnlyList<WorkspaceCropData>>([]);

    public async Task DiscardScanAsync(int scanIndex)
    {
        await DeleteScanAtAsync(scanIndex);
        RefreshStages();
    }

    /// <summary>
    /// Scans a new page to replace the scan at <paramref name="scanIndex"/> (e.g. a crooked or blurry one). The old
    /// scan is only removed once the new one has been scanned, and the new one takes its place in the list.
    /// </summary>
    public async Task RescanAsync(int scanIndex)
    {
        if (scanIndex < 0 || scanIndex >= ScanSessions.Count) return;

        string oldPath = ScanSessions[scanIndex].FilePath;
        string? workDir = WorkDirectory;
        var workspace = Workspace;
        int oldEntryIndex = workspace == null || workDir == null ? -1 : IndexOfEntry(workspace, workDir, oldPath);

        var newPaths = await ScanNewAsync();
        if (newPaths.Count == 0)
        {
            RefreshStages();
            return; // nothing scanned (cancelled or failed): keep the original
        }

        await DeleteScanAtAsync(scanIndex);

        // New scans were appended; move them (and their project entries) to where the old scan was.
        int firstNew = ScanSessions.Count - newPaths.Count;
        for (int k = 0; k < newPaths.Count; k++)
        {
            _sessionManager.Move(firstNew + k, Math.Min(scanIndex + k, ScanSessions.Count - 1));
        }

        if (workspace != null && workDir != null && oldEntryIndex >= 0)
        {
            for (int k = 0; k < newPaths.Count; k++)
            {
                int current = IndexOfEntry(workspace, workDir, newPaths[k]);
                if (current < 0) continue;
                var entry = workspace.Scans[current];
                workspace.Scans.RemoveAt(current);
                workspace.Scans.Insert(Math.Min(oldEntryIndex + k, workspace.Scans.Count), entry);
            }
            ProjectWorkspaceService.SaveSession(workDir, workspace);
        }

        CurrentIndex = Math.Min(scanIndex, ScanSessions.Count - 1);
        RefreshStages();
        ShowStatus(LocalizationService.Format(ResourceKeys.MsgScanReplaced, "Replaced '{0}' with a new scan.", Path.GetFileName(oldPath)));
    }

    private static int IndexOfEntry(WorkspaceSessionState workspace, string workDir, string scanPath)
    {
        string relative = Path.GetRelativePath(workDir, scanPath);
        for (int i = 0; i < workspace.Scans.Count; i++)
        {
            if (string.Equals(workspace.Scans[i].RelativePath, relative, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    /// <summary>Asks for scan files and adds them to the project (see <see cref="ImportScanFilesAsync"/>).</summary>
    public async Task ImportScansAsync()
    {
        if (await EnsureProjectAsync(useDefaultIfCancelled: false) == null) return;
        var paths = await PickImageFilesAsync(LocalizationService.GetString(ResourceKeys.BtnImportScans, "Import scans…"));
        if (paths.Count == 0) return;
        ImportScanFiles(paths);
    }

    /// <summary>
    /// Adds scan files (picked or dropped). In a project they are copied into RawScans/ first, like scanned pages,
    /// so the project keeps its own copy.
    /// </summary>
    private void ImportScanFiles(IEnumerable<string> paths)
    {
        var collected = ImageFileCollector.CollectFiles(paths, recursive: false);
        var workspace = Workspace;
        string? workDir = WorkDirectory;
        var added = new List<string>();

        foreach (string path in collected)
        {
            if (workspace != null && workDir != null)
            {
                string staged = ProjectWorkspaceService.StageRawScanFromFile(workDir, path);
                workspace.Scans.Add(new WorkspaceScanEntry
                {
                    RelativePath = Path.GetRelativePath(workDir, staged),
                    OriginalFileName = Path.GetFileName(path),
                    StagedAtUtc = DateTime.UtcNow
                });
                added.Add(staged);
            }
            else
            {
                added.Add(path);
            }
        }

        if (workspace != null && workDir != null) ProjectWorkspaceService.SaveSession(workDir, workspace);

        var options = _cropsVm.GetDetectionOptions();
        bool wasEmpty = ScanSessions.Count == 0;
        _sessionManager.AddRange([.. added.Select(p => new ScanSessionItem(p, options, isSaved: false, isModified: true))]);
        if (wasEmpty && ScanSessions.Count > 0) CurrentIndex = 0;

        UpdateEmptyStateVisibility();
        RefreshStages();
    }

    public void OpenScanInCrops(int scanIndex)
    {
        if (scanIndex < 0 || scanIndex >= ScanSessions.Count) return;
        CurrentIndex = scanIndex;
        _ = SetStageAsync(AppStage.Crops);
    }

    public Task<IReadOnlyList<string>> PickPhotosToImportAsync() =>
        PickImageFilesAsync(LocalizationService.GetString(ResourceKeys.BtnImportPhotos, "Import photos…"));

    private async Task<IReadOnlyList<string>> PickImageFilesAsync(string title)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return [];

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Images") { Patterns = ["*.jpg", "*.jpeg", "*.png", "*.tif", "*.tiff", "*.bmp", "*.webp"] }
            ]
        });

        return [.. files.Select(f => f.Path.LocalPath)];
    }

    public FinalRenderOptions GetRenderOptions()
    {
        var settings = SettingsManager.Instance.Settings;
        return new FinalRenderOptions
        {
            Format = settings.PreferredFormat,
            JpegQuality = settings.JpegQuality,
            FileNamePattern = settings.FileNamePattern,
            DefaultMetadata = settings.ApplyYearToAllScans
                ? new Core.Models.PhotoExportMetadata { Year = settings.DefaultYear, Description = settings.DefaultDescription }
                : null,
            OutputDirectory = string.IsNullOrWhiteSpace(settings.CustomOutputDirectory) ? null : settings.CustomOutputDirectory
        };
    }

    public RestorationSettings DefaultRestoration
    {
        get
        {
            var settings = SettingsManager.Instance.Settings;
            return new RestorationSettings { RestoreColors = settings.RestoreVintageColors, RemoveDust = settings.RemoveDustAndScratches };
        }
    }

    public Task<bool> RunBusyAsync(string message, Func<Task> work) => ExecuteWithLoadingAsync(message, work);

    public void SaveWorkspace()
    {
        var workspace = Workspace;
        string? workDir = WorkDirectory;
        if (workspace != null && workDir != null)
        {
            ProjectWorkspaceService.SaveSession(workDir, workspace);
        }
        UpdateStageBar();
    }

    public void ShowStatus(string message) => lblStatus.Text = message;

    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        _ = UrlLauncher.OpenUrlAsync(this, new Uri(Path.GetFullPath(path)));
    }
}
