using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhotoCropper.Core;
using PhotoCropper.Core.Scanning;
using PhotoCropper.Core.Workspace;

using PhotoCropper.Gui.Services;
using PhotoCropper.Gui.ViewModels;
using System.Diagnostics.CodeAnalysis;

namespace PhotoCropper.Gui;


[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Avalonia Window lifecycle is managed by OnClosed override")]
internal sealed partial class MainWindow : Window
{
    private readonly ScanSessionManager _sessionManager = new();
    private int CurrentIndex
    {
        get => _sessionManager.CurrentIndex;
        set => _sessionManager.MoveTo(value);
    }
    private IReadOnlyList<ScanSessionItem> ScanSessions => _sessionManager.Sessions;
    private readonly UndoRedoHistory undoHistory = new();
    private readonly AppNotificationService _notificationService = new();
    private bool isLoading;
    private bool isComparingRaw;
    private bool _isExitingConfirmed;
    private readonly CropsStageViewModel _cropsVm;
    private bool _isNavigating => _cropsVm.IsNavigating;
    private CancellationTokenSource? _activeOperationCts;

    public MainWindow() : this(new Naps2ScannerService())
    {
    }

    public MainWindow(IScannerService scannerService)
    {
        ArgumentNullException.ThrowIfNull(scannerService);

        _scannerService = scannerService;

        InitializeComponent();
        _notificationService.Initialize(this);

        _cropsVm = new CropsStageViewModel(this, _sessionManager, undoHistory, () => SettingsManager.Instance.Settings, SettingsManager.Instance.Save);
        grdMainWorkspace.DataContext = _cropsVm;

        // Invalidate undo/redo entries for a scan when its engine is deactivated.
        // Re-activating the engine recreates it from scratch (DetectPhotos), so any
        // previously recorded actions targeting that scan would apply to stale state.
        _sessionManager.SessionDeactivated += scanIndex => undoHistory.InvalidateScan(scanIndex);

        // Register key handlers in Tunnel phase
        AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, Window_KeyUp, RoutingStrategies.Tunnel);

        // Register Drag & Drop event handlers
        Closing += Window_Closing;
        Loaded += MainWindow_Loaded;

        PopulateLanguageMenu();
        ApplySettingsToUi();
        InitializeStages();
    }

    private void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_isExitingConfirmed) return;

        if (HasUnsavedChanges())
        {
            e.Cancel = true;
            if (pnlUnsavedOverlay != null)
            {
                pnlUnsavedOverlay.IsVisible = true;
            }
        }
    }

    private async void BtnSaveAndExit_Click(object? sender, RoutedEventArgs e)
    {
        if (pnlUnsavedOverlay != null) pnlUnsavedOverlay.IsVisible = false;
        await SavePendingScansAsync(closeAfterSave: true);
    }

    private void BtnDiscardAndExit_Click(object? sender, RoutedEventArgs e)
    {
        if (pnlUnsavedOverlay != null) pnlUnsavedOverlay.IsVisible = false;
        _isExitingConfirmed = true;
        Close();
    }

    private void BtnCancelExit_Click(object? sender, RoutedEventArgs e)
    {
        if (pnlUnsavedOverlay != null) pnlUnsavedOverlay.IsVisible = false;
    }

    /// <summary>
    /// How long a cancelled operation gets to actually stop before the UI gives up waiting on it.
    /// Most operations honour the token well within this; the grace period only expires for work
    /// that ignores cancellation, such as a scanner driver that has hung.
    /// </summary>
    internal static readonly TimeSpan CancellationGracePeriod = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Runs <paramref name="action"/> behind the loading overlay.
    /// </summary>
    /// <param name="propagateExceptions">
    /// When true, failures (including cancellation) are rethrown to the caller after the overlay is
    /// closed, so it can show its own error handling. When false they are reported generically.
    /// </param>
    /// <returns>True when the action ran to completion; false if it was skipped, cancelled or failed.</returns>
    private async Task<bool> ExecuteWithLoadingAsync(
        string statusText,
        Func<CancellationToken, Task> action,
        string? completionText = null,
        bool canCancel = false,
        TimeSpan? timeout = null,
        bool propagateExceptions = false)
    {
        if (isLoading) return false;
        isLoading = true;
        pnlLoadingOverlay.IsVisible = true;
        btnCancelLoading.IsVisible = canCancel;
        btnCancelLoading.IsEnabled = true;
        txtLoadingText.Text = statusText;
        lblStatus.Text = statusText;

        var cts = timeout.HasValue
            ? new CancellationTokenSource(timeout.Value)
            : new CancellationTokenSource();
        _activeOperationCts = cts;

        bool abandoned = false;

        try
        {
            // On cancel, keep the overlay up (showing "Cancelling...") until the operation actually stops,
            // so a second operation can't start on top of it.
            var outcome = await CancellableOperationRunner.RunAsync(action, cts, CancellationGracePeriod, () =>
            {
                txtLoadingText.Text = LocalizationService.GetString(ResourceKeys.TxtCancelling, "Cancelling...");
                btnCancelLoading.IsEnabled = false;
            }).ConfigureAwait(true);

            if (outcome == OperationOutcome.Abandoned)
            {
                // The operation ignores cancellation (e.g. a hung scanner driver) and was left running;
                // the runner now owns the CTS. Callers must discard results from a cancelled token.
                abandoned = true;
                throw new OperationCanceledException(cts.Token);
            }

            if (completionText != null)
            {
                lblStatus.Text = completionText;
            }

            return true;
        }
        catch (OperationCanceledException) when (!propagateExceptions)
        {
            lblStatus.Text = LocalizationService.GetString(ResourceKeys.MsgScanCancelled, "Operation cancelled.");
            return false;
        }
        catch (Exception ex) when (!propagateExceptions)
        {
            ShowAppError("Operation Failed", ex.Message);
            return false;
        }
        finally
        {
            _activeOperationCts = null;
            btnCancelLoading.IsVisible = false;
            pnlLoadingOverlay.IsVisible = false;
            isLoading = false;

            // An abandoned operation still holds the token, so its source is disposed when it ends.
            if (!abandoned)
            {
                cts.Dispose();
            }
        }
    }

    internal void ShowAppError(string title, string message)
    {
        if (txtErrorTitle != null) txtErrorTitle.Text = title;
        if (txtErrorMessage != null) txtErrorMessage.Text = message;
        if (pnlErrorOverlay != null) pnlErrorOverlay.IsVisible = true;
    }

    private Task<bool> ExecuteWithLoadingAsync(string statusText, Func<Task> action, string? completionText = null)
    {
        return ExecuteWithLoadingAsync(statusText, _ => action(), completionText, canCancel: false);
    }

    private void BtnCancelLoading_Click(object? sender, RoutedEventArgs e)
    {
        btnCancelLoading.IsEnabled = false;
        txtLoadingText.Text = LocalizationService.GetString(ResourceKeys.TxtCancelling, "Cancelling...");
        _activeOperationCts?.Cancel();
    }

    private void ToggleMenuBar()
    {
        if (pnlMenuBar != null)
        {
            pnlMenuBar.IsVisible = !pnlMenuBar.IsVisible;
        }
    }

    private void MenuExit_Click(object? sender, RoutedEventArgs e) => Close();
    private async void MenuUndo_Click(object? sender, RoutedEventArgs e) => await PerformUndoAsync();
    private async void MenuRedo_Click(object? sender, RoutedEventArgs e) => await PerformRedoAsync();
    private void MenuViewCarousel_Click(object? sender, RoutedEventArgs e) => SetViewMode(false);
    private void MenuViewGrid_Click(object? sender, RoutedEventArgs e) => SetViewMode(true);

    private void BtnReset_Click(object? sender, RoutedEventArgs e) => BtnResetDefaults_Click(sender, e);

    private Dialogs.HelpWindow? _helpWindow;

    private void ShowHelpWindow()
    {
        if (_helpWindow != null && _helpWindow.IsVisible)
        {
            _helpWindow.Activate();
            return;
        }

        _helpWindow = new Dialogs.HelpWindow();
        _helpWindow.Closed += (_, _) => _helpWindow = null;
        _helpWindow.Show(this);
    }

    private void BtnHelp_Click(object? sender, RoutedEventArgs e) => ShowHelpWindow();

    internal static bool IsTextInputActive(IInputElement? focusedElement, object? sourceElement)
    {
        if (focusedElement is TextBox) return true;
        if (sourceElement is TextBox) return true;
        if (sourceElement is Visual v && v.FindAncestorOfType<TextBox>() != null) return true;
        return false;
    }

    private async void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (isLoading)
        {
            e.Handled = true;
            return;
        }

        if (HandleTextInputGuard(e)) return;

        // The editing shortcuts act on the scan editor, which only the Crops stage shows. Elsewhere only the
        // global keys apply (F1 help, F5 scan); everything else goes to the stage's own controls.
        if (_stage != ViewModels.AppStage.Crops && e.Key is not (Key.F1 or Key.F5)) return;

        if (await HandleReviewKey(e)) return;
        if (await HandleRefinementKey(e)) return;
        if (await HandleEditingKey(e)) return;
        if (HandleViewKey(e)) return;
        if (await HandleNavigationKey(e)) return;
    }

    private bool HandleTextInputGuard(KeyEventArgs e)
    {
        if (!IsTextInputActive(FocusManager?.GetFocusedElement(), e.Source)) return false;

        if (e.Key == Key.Escape || e.Key == Key.Enter)
        {
            FocusManager?.Focus(null);
            e.Handled = true;
            return true;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.S)
            {
                BtnSaveImages_Click(null, new RoutedEventArgs());
                e.Handled = true;
                return true;
            }

        }

        return true;
    }

    private async ValueTask<bool> HandleRefinementKey(KeyEventArgs e)
    {
        if (!isRefining) return false;

        switch (e.Key)
        {
            case Key.Enter:
            case Key.A:
                await AcceptRefineAsync();
                e.Handled = true;
                break;
            case Key.Back:
            case Key.Escape:
            case Key.C:
                RejectRefine();
                e.Handled = true;
                break;
        }
        return true;
    }

    private async ValueTask<bool> HandleEditingKey(KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            switch (e.Key)
            {
                case Key.S:
                    BtnSaveImages_Click(null, new RoutedEventArgs());
                    e.Handled = true;
                    return true;

                case Key.Z:
                    await PerformUndoAsync();
                    e.Handled = true;
                    return true;
                case Key.Y:
                    await PerformRedoAsync();
                    e.Handled = true;
                    return true;
            }
        }

        if (ScanSessions.Count > 0)
        {
            if (e.Key == Key.Delete && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                await DeleteCurrentScanAsync();
                e.Handled = true;
                return true;
            }

            switch (e.Key)
            {
                case Key.N:
                case Key.Oem3:
                    FocusManager?.Focus(null);
                    StartRefineMode();
                    e.Handled = true;
                    return true;
                case Key.R:
                    FocusManager?.Focus(null);
                    await RotateSelectedPhotosAsync();
                    e.Handled = true;
                    return true;
                case Key.V:
                    FocusManager?.Focus(null);
                    await OpenReviewModeAsync();
                    e.Handled = true;
                    return true;
                case Key.X:
                case Key.Delete:
                    FocusManager?.Focus(null);
                    DeleteSelectedPhotos();
                    e.Handled = true;
                    return true;
            }
        }

        return false;
    }

    private bool HandleViewKey(KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.A && _cropsVm.IsGridView)
        {
            SelectAllGalleryPhotos();
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.F1)
        {
            ShowHelpWindow();
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.F5)
        {
            BtnScan_Click(null, new RoutedEventArgs());
            e.Handled = true;
            return true;
        }

        if (!isLoading && !_isNavigating && ScanSessions.Count > 0 && (e.Key == Key.Space || e.Key == Key.B))
        {
            if (!isComparingRaw && _cropsVm.SelectedPhotoIndex >= 0)
            {
                isComparingRaw = true;
                _cropsVm.ShowOriginal(true);
            }
            e.Handled = true;
            return true;
        }

        return false;
    }

    private async ValueTask<bool> HandleNavigationKey(KeyEventArgs e)
    {
        if (e.Key == Key.LeftAlt || e.Key == Key.RightAlt || e.Key == Key.F10)
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                ToggleMenuBar();
                e.Handled = true;
                return true;
            }
        }

        if (e.Key == Key.Escape)
        {
            if (pnlMenuBar != null && pnlMenuBar.IsVisible)
            {
                pnlMenuBar.IsVisible = false;
                e.Handled = true;
                return true;
            }

            if (pnlUnsavedOverlay != null && pnlUnsavedOverlay.IsVisible)
            {
                pnlUnsavedOverlay.IsVisible = false;
                e.Handled = true;
                return true;
            }

            if (pnlErrorOverlay.IsVisible)
            {
                pnlErrorOverlay.IsVisible = false;
                e.Handled = true;
                return true;
            }

            if (pnlLoadingOverlay.IsVisible && btnCancelLoading.IsVisible)
            {
                btnCancelLoading.IsEnabled = false;
                txtLoadingText.Text = LocalizationService.GetString(ResourceKeys.TxtCancelling, "Cancelling...");
                _activeOperationCts?.CancelAsync();
                e.Handled = true;
                return true;
            }

            if (_cropsVm.IsGridView && _cropsVm.SelectedPhotoIndices().Count > 1)
            {
                ClearGallerySelection();
                e.Handled = true;
                return true;
            }
            return false;
        }

        if (ScanSessions.Count > 0)
        {
            switch (e.Key)
            {
                case Key.Up:
                case Key.PageUp:
                    if (isLoading || _isNavigating) { e.Handled = true; return true; }
                    e.Handled = true;
                    await NavigateScanAsync(forward: false);
                    return true;

                case Key.Down:
                case Key.PageDown:
                    if (isLoading || _isNavigating) { e.Handled = true; return true; }
                    e.Handled = true;
                    await NavigateScanAsync(forward: true);
                    return true;

                case Key.Left:
                    if (!isLoading && !_isNavigating)
                    {
                        FocusManager?.Focus(null);
                        _cropsVm.PreviousPhotoCommand.Execute(null);
                    }
                    e.Handled = true;
                    return true;

                case Key.Right:
                    if (!isLoading && !_isNavigating)
                    {
                        FocusManager?.Focus(null);
                        _cropsVm.NextPhotoCommand.Execute(null);
                    }
                    e.Handled = true;
                    return true;
            }
        }

        return false;
    }

    /// <summary>Moves to the next or previous scan and opens it.</summary>
    private Task NavigateScanAsync(bool forward)
    {
        FocusManager?.Focus(null);
        return _cropsVm.NavigateAsync(forward);
    }

    private Task NavigateToScanIndexAsync(int targetIndex)
    {
        FocusManager?.Focus(null);
        return _cropsVm.NavigateToAsync(targetIndex);
    }

    private void Window_KeyUp(object? sender, KeyEventArgs e)
    {
        if (IsTextInputActive(FocusManager?.GetFocusedElement(), e.Source))
        {
            return;
        }

        if (isComparingRaw && (e.Key == Key.Space || e.Key == Key.B))
        {
            isComparingRaw = false;
            if (!isLoading && !_isNavigating && ScanSessions.Count > 0)
            {
                _cropsVm.ShowOriginal(false);
            }
            e.Handled = true;
        }
    }

    /// <summary>The scan currently shown, so editing boxes only repaints the overlay instead of re-uploading the scan.</summary>
    private Emgu.CV.Mat? _shownScan;

    internal void SetMainImage(Emgu.CV.Mat? mat)
    {
        if (img == null) return;

        var oldSource = img.Source as IDisposable;
        img.Source = mat != null ? MatBitmapConverter.ToAvaloniaBitmap(mat) : null;
        _shownScan = mat;
        DeferDispose(oldSource);
        UpdateCropCanvasSize();
        if (mat == null) ShowCropBoxes(null);
    }

    /// <summary>Shows a scan with its boxes drawn over it. The bitmap is only rebuilt when the scan itself changed.</summary>
    internal void ShowScan(PhotoCropperEngine? engine)
    {
        var scan = engine?.Original;
        if (scan == null || scan.IsEmpty)
        {
            SetMainImage(null);
            return;
        }

        if (!ReferenceEquals(scan, _shownScan)) SetMainImage(scan);
        ShowCropBoxes(engine);
    }

    private void ShowCropBoxes(PhotoCropperEngine? engine)
    {
        if (cropOverlay == null) return;
        if (engine?.Original == null || engine.Original.IsEmpty)
        {
            cropOverlay.Candidates = null;
            return;
        }

        cropOverlay.ScanSize = new Size(engine.Original.Width, engine.Original.Height);
        cropOverlay.OverlayScale = engine.OverlayScale;
        var bgr = engine.CurrentOptions.GetBoundingBoxColorBgr();
        cropOverlay.BoxColor = Color.FromRgb((byte)bgr.V2, (byte)bgr.V1, (byte)bgr.V0);
        cropOverlay.Candidates = engine.AcceptedCandidates;
        // The engine may have edited the same list instance in place; repaint either way.
        cropOverlay.InvalidateVisual();
    }

    internal void SetRefineImage(Emgu.CV.Mat? mat)
    {
        if (imgRefine == null) return;

        var oldSource = imgRefine.Source as IDisposable;
        imgRefine.Source = mat != null ? MatBitmapConverter.ToAvaloniaBitmap(mat) : null;
        DeferDispose(oldSource);
    }

    internal static void DeferDispose(IDisposable? obj, int delayMs = 300)
    {
        if (obj == null) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(delayMs);
            Dispatcher.UIThread.Post(obj.Dispose);
        });
    }

    internal static void DeferDispose(IEnumerable<IDisposable> objects, int delayMs = 300)
    {
        var list = objects.ToList();
        if (list.Count == 0) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(delayMs);
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var obj in list) obj.Dispose();
            });
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        var settings = SettingsManager.Instance.Settings;
        settings.BackgroundTolerance = PhotoCropper.Core.Models.DetectionOptions.SensitivityToTolerance(_cropsVm.Sensitivity);
        settings.ZoomLevel = sldZoom.Value;
        settings.MinAreaFactor = _cropsVm.MinAreaPercent;
        settings.MaxAreaFactor = _cropsVm.MaxAreaPercent;
        settings.CannyLowThreshold = _cropsVm.EdgeThreshold;

        if (!string.IsNullOrEmpty(settings.WorkDirectory) && Directory.Exists(settings.WorkDirectory) && _workspaceSession != null)
        {
            ProjectWorkspaceService.SaveSession(settings.WorkDirectory, _workspaceSession);
        }

        SettingsManager.Instance.Save();

        base.OnClosed(e);
        SetMainImage(null);
        SetRefineImage(null);
        ClearGalleryBitmaps();
        _scannerService.Dispose();
        undoHistory.Dispose();
        _sessionManager.Dispose();
        PhotoCropper.Core.Extraction.FaceOrientationService.ReleaseCachedDetectors();
        _finishStageVm?.Dispose();
        _cropsVm.Dispose();
    }
}
