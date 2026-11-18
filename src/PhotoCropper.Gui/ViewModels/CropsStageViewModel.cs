using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoCropper.Core;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Models;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;
using System.Diagnostics;

namespace PhotoCropper.Gui.ViewModels;

/// <summary>What the Crops stage needs from the window. The scan editor itself (image, boxes, gallery) still lives there.</summary>
internal interface ICropsStageHost
{
    /// <summary>True while the loading overlay is up; the stage starts no work then.</summary>
    bool IsBusy { get; }

    /// <summary>Runs work behind the loading overlay; returns false if it failed or was skipped.</summary>
    Task<bool> RunBusyAsync(string message, Func<CancellationToken, Task> work);

    /// <summary>Shows a scan that was loaded or re-detected: the image with its boxes, the photo list and counters.</summary>
    void ShowScanResult(PhotoCropperEngine engine);

    /// <summary>Repaints just the boxes over the scan, while one is being dragged.</summary>
    void ShowBoxes(PhotoCropperEngine engine);

    /// <summary>Selects a photo of the open scan in the photo list.</summary>
    void SelectPhoto(int index);

    void ShowStatus(string message);
}

/// <summary>
/// The Crops stage's scan-level state: which scan is open, the detection settings (sensitivity, photo size, edge
/// threshold, auto-orient, auto-tune), detection coverage, and the background look-ahead that prepares the scans
/// around the current one so moving between them is instant. Box editing and the photo list are still in the window.
/// </summary>
internal sealed partial class CropsStageViewModel : ObservableObject, IDisposable
{
    private readonly ICropsStageHost _host;
    private readonly ScanSessionManager _sessions;
    private readonly Func<UserSettings> _settings;
    private readonly Action _saveSettings;
    private readonly UndoRedoHistory _undo;

    // Lets the background look-ahead and navigation take turns on an engine; see DrainLookaheadAsync.
    private readonly SemaphoreSlim _lookaheadLock = new(1, 1);
    private CancellationTokenSource? _lookaheadCts;
    private CancellationTokenSource? _optionsDebounceCts;
    private bool _isShowingOptions;

    // Restoration moved to the Finish stage, but scans still carry these options; they are passed through unchanged.
    private bool _restoreColors;
    private bool _removeDust;

    public CropsStageViewModel(ICropsStageHost host, ScanSessionManager sessions, UndoRedoHistory undo, Func<UserSettings> settings, Action saveSettings)
    {
        _host = host;
        _sessions = sessions;
        _undo = undo;
        _settings = settings;
        _saveSettings = saveSettings;
        LoadFromSettings();
    }

    /// <summary>How long option changes (e.g. dragging a slider) wait before detection runs again.</summary>
    public TimeSpan OptionsDebounce { get; init; } = TimeSpan.FromMilliseconds(150);

    [ObservableProperty] private double _sensitivity = 50;
    [ObservableProperty] private double _minAreaPercent = 25;
    [ObservableProperty] private double _maxAreaPercent = 90;
    [ObservableProperty] private double _edgeThreshold = 20;
    [ObservableProperty] private bool _autoOrient = true;
    [ObservableProperty] private bool _autoTuneOnScanChange;
    [ObservableProperty] private bool _autoAdjustOnLowCoverage = true;

    [ObservableProperty] private string _scanCounterText = string.Empty;
    [ObservableProperty] private string _coverageText = string.Empty;

    /// <summary>The open scan was prepared in the background, so it opened without waiting.</summary>
    [ObservableProperty] private bool _isPreloaded;

    /// <summary>Detection is about to re-run after an option change (shown as a small indicator on the scan).</summary>
    [ObservableProperty] private bool _isDetectionPending;

    /// <summary>Set while moving to another scan, so key repeats and clicks don't start a second move.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousScanCommand), nameof(NextScanCommand))]
    private bool _isNavigating;

    private IReadOnlyList<ScanSessionItem> Scans => _sessions.Sessions;
    private ScanSessionItem? Current => _sessions.CurrentSession;

    private bool CanStartWork => !_host.IsBusy && !IsNavigating && Current != null;

    // ---- settings ----

    /// <summary>Shows the saved detection settings (on start, and after "Reset defaults").</summary>
    public void LoadFromSettings()
    {
        var s = _settings();
        _isShowingOptions = true;
        try
        {
            Sensitivity = DetectionOptions.ToleranceToSensitivity(s.BackgroundTolerance);
            MinAreaPercent = s.MinAreaFactor;
            MaxAreaPercent = s.MaxAreaFactor;
            EdgeThreshold = s.CannyLowThreshold;
            AutoOrient = s.AutoOrientPhotos;
            AutoTuneOnScanChange = s.AutoTuneOnScanChange;
            AutoAdjustOnLowCoverage = s.AutoAdjustOnLowCoverage;
            _restoreColors = s.RestoreVintageColors;
            _removeDust = s.RemoveDustAndScratches;
        }
        finally
        {
            _isShowingOptions = false;
        }
    }

    /// <summary>Shows a scan's own detection options without treating it as a change.</summary>
    public void ShowOptions(DetectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var s = _settings();
        _isShowingOptions = true;
        try
        {
            Sensitivity = DetectionOptions.ToleranceToSensitivity(options.BackgroundTolerance);
            MinAreaPercent = options.MinAreaFactor * 100.0;
            MaxAreaPercent = options.MaxAreaFactor * 100.0;
            EdgeThreshold = options.CannyLowThreshold;
            AutoOrient = options.AutoOrientPhotos;
            _restoreColors = options.RestoreVintageColors;
            _removeDust = options.RemoveDustAndScratches;
            AutoTuneOnScanChange = s.AutoTuneOnScanChange;
            AutoAdjustOnLowCoverage = s.AutoAdjustOnLowCoverage;
        }
        finally
        {
            _isShowingOptions = false;
        }
    }

    /// <summary>The detection options as currently set, for the open scan and for scans added now.</summary>
    public DetectionOptions GetDetectionOptions() => new()
    {
        BackgroundTolerance = DetectionOptions.SensitivityToTolerance(Sensitivity),
        MinAreaFactor = MinAreaPercent / 100.0,
        MaxAreaFactor = MaxAreaPercent / 100.0,
        CannyLowThreshold = EdgeThreshold,
        CannyHighThreshold = EdgeThreshold * AppConstants.DefaultCannyHighRatio,
        AutoOrientPhotos = AutoOrient,
        RestoreVintageColors = _restoreColors,
        RemoveDustAndScratches = _removeDust,
        BoundingBoxColor = _settings().DetectionBoxColor,
    };

    partial void OnSensitivityChanged(double value) => OnDetectionSliderChanged();
    partial void OnMinAreaPercentChanged(double value) => OnDetectionSliderChanged();
    partial void OnMaxAreaPercentChanged(double value) => OnDetectionSliderChanged();
    partial void OnEdgeThresholdChanged(double value) => OnDetectionSliderChanged();

    partial void OnAutoOrientChanged(bool value)
    {
        if (_isShowingOptions || !CanStartWork) return;
        _settings().AutoOrientPhotos = value;
        _saveSettings();
        Current!.Options.AutoOrientPhotos = value;
        _ = ReprocessAsync();
    }

    partial void OnAutoTuneOnScanChangeChanged(bool value)
    {
        if (_isShowingOptions) return;
        _settings().AutoTuneOnScanChange = value;
        _saveSettings();
        if (value) _ = AutoTuneAsync();
    }

    partial void OnAutoAdjustOnLowCoverageChanged(bool value)
    {
        if (_isShowingOptions) return;
        _settings().AutoAdjustOnLowCoverage = value;
        _saveSettings();
        if (value && Current?.Engine is { } engine && engine.TotalDetectedAreaRatio < AppConstants.LowCoverageThreshold)
        {
            _ = AutoTuneAsync();
        }
    }

    /// <summary>Slider moves are remembered at once but detection waits until the user pauses dragging.</summary>
    private async void OnDetectionSliderChanged()
    {
        if (_isShowingOptions || !CanStartWork) return;

        var settings = _settings();
        settings.BackgroundTolerance = DetectionOptions.SensitivityToTolerance(Sensitivity);
        settings.MinAreaFactor = MinAreaPercent;
        settings.MaxAreaFactor = MaxAreaPercent;
        settings.CannyLowThreshold = EdgeThreshold;

        var options = Current!.Options;
        options.BackgroundTolerance = settings.BackgroundTolerance;
        options.MinAreaFactor = MinAreaPercent / 100.0;
        options.MaxAreaFactor = MaxAreaPercent / 100.0;
        options.CannyLowThreshold = EdgeThreshold;
        options.CannyHighThreshold = EdgeThreshold * AppConstants.DefaultCannyHighRatio;

        _optionsDebounceCts?.Cancel();
        _optionsDebounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _optionsDebounceCts = cts;

        IsDetectionPending = true;
        try
        {
            await Task.Delay(OptionsDebounce, cts.Token);
            _saveSettings();
            await ReprocessAsync();
        }
        catch (OperationCanceledException)
        {
            // Still dragging: a newer change will run detection.
        }
        finally
        {
            if (!cts.IsCancellationRequested) IsDetectionPending = false;
        }
    }

    // ---- scans ----

    /// <summary>
    /// Opens the current scan: at once when the look-ahead already prepared it, otherwise behind the loading
    /// overlay (auto-tuning first when the settings ask for it). Then starts preparing the scans around it.
    /// </summary>
    public async Task LoadCurrentScanAsync()
    {
        var session = Current;
        if (session == null || _host.IsBusy)
        {
            UpdateScanCounter();
            return;
        }

        string fileName = Path.GetFileName(session.FilePath);
        IsPreloaded = session.IsActive && session.IsAutoTuned;

        if (IsPreloaded)
        {
            ShowLoaded(session.Activate(), fileName);
        }
        else
        {
            string processing = LocalizationService.GetString(ResourceKeys.ProcessingScan, "Processing...");
            await _host.RunBusyAsync($"{processing} {fileName}", async ct =>
            {
                var engine = await Task.Run(session.Activate, ct);
                if (ShouldAutoTune(session, engine))
                {
                    var result = await Task.Run(() => engine.AutoTune(), ct);
                    if (result.Improved || result.PhotoCount > 0) session.IsModified = true;
                }
                session.IsAutoTuned = true;
                ShowLoaded(engine, fileName);
            });
        }

        UpdateScanCounter();
        PreviousScanCommand.NotifyCanExecuteChanged();
        NextScanCommand.NotifyCanExecuteChanged();
        StartLookahead();
    }

    private void ShowLoaded(PhotoCropperEngine engine, string fileName)
    {
        ShowOptions(engine.CurrentOptions);
        _host.ShowScanResult(engine);
        UpdateScanCounter();
        UpdateCoverage();
        _host.ShowStatus(fileName);
    }

    private bool ShouldAutoTune(ScanSessionItem session, PhotoCropperEngine engine)
    {
        if (session.IsAutoTuned || session.IsSaved) return false;
        var settings = _settings();
        return settings.AutoTuneOnScanChange ||
               (settings.AutoAdjustOnLowCoverage && engine.TotalDetectedAreaRatio < AppConstants.LowCoverageThreshold);
    }

    private bool CanGoPrevious() => !IsNavigating && Scans.Count > 1 && _sessions.CurrentIndex > 0;
    private bool CanGoNext() => !IsNavigating && Scans.Count > 1 && _sessions.CurrentIndex < Scans.Count - 1;

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private Task PreviousScanAsync() => NavigateAsync(forward: false);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private Task NextScanAsync() => NavigateAsync(forward: true);

    /// <summary>Moves to the next or previous scan and opens it.</summary>
    public Task NavigateAsync(bool forward) => MoveAndLoadAsync(() => forward ? _sessions.MoveNext() : _sessions.MovePrevious());

    /// <summary>Opens the scan at <paramref name="index"/>.</summary>
    public Task NavigateToAsync(int index)
    {
        if (index < 0 || index >= Scans.Count || index == _sessions.CurrentIndex) return Task.CompletedTask;
        return MoveAndLoadAsync(() => _sessions.MoveTo(index));
    }

    /// <summary>
    /// <see cref="IsNavigating"/> is set before the first await so re-entrant key events bail out at once; the
    /// look-ahead is drained first so it never runs AutoTune on the engine this load is about to use.
    /// </summary>
    private async Task MoveAndLoadAsync(Func<bool> move)
    {
        if (IsNavigating || _host.IsBusy || Scans.Count == 0) return;
        IsNavigating = true;
        CancelGesture();
        try
        {
            await DrainLookaheadAsync();
            if (move()) await LoadCurrentScanAsync();
        }
        finally
        {
            IsNavigating = false;
        }
    }

    /// <summary>Runs detection again on the open scan with the current options.</summary>
    public async Task ReprocessAsync()
    {
        if (!CanStartWork) return;

        var session = Current!;
        session.IsModified = true;
        var engine = session.Activate();
        engine.ApplyOptions(GetDetectionOptions());

        await _host.RunBusyAsync(LocalizationService.GetString(ResourceKeys.MsgDetectingPhotos, "Detecting photos..."), async ct =>
        {
            await Task.Run(engine.DetectPhotos, ct);
            _host.ShowScanResult(engine);
            UpdateCoverage();
            _host.ShowStatus(LocalizationService.Format(ResourceKeys.MsgDetectionComplete, "Detection complete. Found {0} photos.", engine.DetectedPhotos.Count));
        });
    }

    /// <summary>Searches for detection settings that find more photos on the open scan.</summary>
    [RelayCommand]
    private async Task AutoTuneAsync()
    {
        if (!CanStartWork) return;

        var session = Current!;
        var engine = session.Activate();
        await _host.RunBusyAsync(LocalizationService.GetString(ResourceKeys.MsgAutoTuning, "Auto-tuning detection parameters..."), async ct =>
        {
            var result = await Task.Run(() => engine.AutoTune(), ct);
            session.IsModified = true;
            ShowOptions(engine.CurrentOptions);
            _host.ShowScanResult(engine);
            UpdateCoverage();

            _host.ShowStatus(result.Improved || result.PhotoCount > 0
                ? LocalizationService.Format(
                    ResourceKeys.MsgAutoTuneSuccess,
                    "Auto-tuned: found {0} photos (Sensitivity: {1:0}%, Edge: {2:0}).",
                    result.PhotoCount,
                    DetectionOptions.ToleranceToSensitivity(result.BestOptions.BackgroundTolerance),
                    result.BestOptions.CannyLowThreshold)
                : LocalizationService.GetString(ResourceKeys.MsgAutoTuneFailed, "Auto-tune did not find additional photos."));
        });
    }

    /// <summary>"Scan 3 of 12", or "No files loaded".</summary>
    public void UpdateScanCounter()
    {
        ScanCounterText = Scans.Count == 0
            ? LocalizationService.GetString(ResourceKeys.TxtNoFiles, "No files loaded")
            : LocalizationService.Format(ResourceKeys.ScanCounter, "Scan {0} of {1}", _sessions.CurrentIndex + 1, Scans.Count);
        if (Scans.Count == 0) IsPreloaded = false;
        PreviousScanCommand.NotifyCanExecuteChanged();
        NextScanCommand.NotifyCanExecuteChanged();
    }

    /// <summary>How much of the open scan its boxes cover; refresh after boxes were added, removed or resized.</summary>
    public void UpdateCoverage()
    {
        CoverageText = Current is { IsActive: true, Engine: { } engine }
            ? LocalizationService.Format(ResourceKeys.LblCoveragePercent, "Coverage: {0}%", (int)Math.Round(engine.TotalDetectedAreaRatio * 100.0))
            : string.Empty;
    }

    // ---- background look-ahead ----

    /// <summary>
    /// Prepares (and auto-tunes, when the settings ask) the scans just ahead of and behind the current one, and
    /// releases scans far from it that have no unsaved edits, so memory stays bounded on large batches.
    /// </summary>
    public void StartLookahead(int ahead = AppConstants.DefaultLookaheadAhead, int behind = AppConstants.DefaultLookaheadBehind)
    {
        int count = Scans.Count;
        int current = _sessions.CurrentIndex;
        if (count == 0 || current < 0 || current >= count) return;

        _lookaheadCts?.Cancel();
        _lookaheadCts?.Dispose();
        var cts = new CancellationTokenSource();
        _lookaheadCts = cts;
        var ct = cts.Token;

        var toPrepare = LookaheadPlan.ScansToPrepare(count, current, ahead, behind)
            .Select(i => Scans[i])
            .Where(s => !s.IsActive || !s.IsAutoTuned)
            .ToList();

        foreach (int i in LookaheadPlan.ScansToRelease(count, current, ahead))
        {
            if (Scans[i].IsActive) Scans[i].TryDeactivateIfUnmodified();
        }

        if (toPrepare.Count == 0) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await _lookaheadLock.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                foreach (var session in toPrepare)
                {
                    if (ct.IsCancellationRequested) break;
                    session.IsProcessing = true;
                    try
                    {
                        var engine = session.Activate();
                        if (ct.IsCancellationRequested) break;
                        if (ShouldAutoTune(session, engine))
                        {
                            var result = engine.AutoTune();
                            if (result.Improved || result.PhotoCount > 0) session.IsModified = true;
                        }
                        session.IsAutoTuned = true;
                    }
                    catch (Exception ex) when (ex is IOException or InvalidOperationException)
                    {
                        Trace.TraceWarning($"[PhotoCropper] Background lookahead failed for session: {ex.GetType().Name}: {ex.Message}");
                    }
                    finally
                    {
                        session.IsProcessing = false;
                    }
                }
            }
            finally
            {
                _lookaheadLock.Release();
            }
        }, ct).ContinueWith(t => Trace.TraceError(t.Exception?.ToString()), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    /// <summary>
    /// Cancels the look-ahead and waits (up to 3 s) until it has let go of its engine, so the caller can move,
    /// load or remove scans without a background task using them at the same time.
    /// </summary>
    public async Task DrainLookaheadAsync()
    {
        if (_lookaheadCts != null) await _lookaheadCts.CancelAsync();
        bool acquired = await _lookaheadLock.WaitAsync(3000, CancellationToken.None);
        if (acquired) _lookaheadLock.Release();
    }

    public void Dispose()
    {
        ClearDragSnapshot();
        _lookaheadCts?.Cancel();
        _lookaheadCts?.Dispose();
        _optionsDebounceCts?.Cancel();
        _optionsDebounceCts?.Dispose();
        _lookaheadLock.Dispose();
    }
}

/// <summary>Which scans the look-ahead prepares and releases around the current one (wrapping around the list).</summary>
internal static class LookaheadPlan
{
    /// <summary>The next <paramref name="ahead"/> scans, then the one <paramref name="behind"/> places back.</summary>
    public static IReadOnlyList<int> ScansToPrepare(int count, int current, int ahead, int behind)
    {
        var result = new List<int>();
        if (count <= 1) return result;
        for (int offset = 1; offset <= ahead; offset++)
        {
            int i = (current + offset) % count;
            if (i != current && !result.Contains(i)) result.Add(i);
        }
        int back = ((current - behind) % count + count) % count;
        if (back != current && !result.Contains(back)) result.Add(back);
        return result;
    }

    /// <summary>Scans further than <paramref name="ahead"/> + 1 from the current one, counting around the list.</summary>
    public static IReadOnlyList<int> ScansToRelease(int count, int current, int ahead)
    {
        var result = new List<int>();
        for (int i = 0; i < count; i++)
        {
            if (i == current) continue;
            int distance = Math.Min(Math.Abs(i - current), count - Math.Abs(i - current));
            if (distance > ahead + 1) result.Add(i);
        }
        return result;
    }
}
