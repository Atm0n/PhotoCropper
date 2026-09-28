using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhotoCropper.Core.Scanning;
using PhotoCropper.Core.Workspace;

using PhotoCropper.Gui.Services;
using System.Diagnostics.CodeAnalysis;

namespace PhotoCropper.Gui;

internal sealed record GalleryPhotoItem(Avalonia.Media.Imaging.Bitmap Image, string Label, string Dimensions, int Index);

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
    private bool isSyncingSelection;
    private bool isUpdatingUiFromScan;
    private bool _isExitingConfirmed;
    private bool _isNavigating;
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

    private async Task ExecuteWithLoadingAsync(
        string statusText,
        Func<CancellationToken, Task> action,
        string? completionText = null,
        bool canCancel = false,
        TimeSpan? timeout = null)
    {
        if (isLoading) return;
        isLoading = true;
        pnlLoadingOverlay.IsVisible = true;
        btnCancelLoading.IsVisible = canCancel;
        btnCancelLoading.IsEnabled = true;
        txtLoadingText.Text = statusText;
        lblStatus.Text = statusText;

        using var cts = timeout.HasValue
            ? new CancellationTokenSource(timeout.Value)
            : new CancellationTokenSource();
        _activeOperationCts = cts;

        var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancelReg = cts.Token.Register(() => cancelTcs.TrySetResult(true));

        try
        {
            var actionTask = action(cts.Token);
            var completedTask = await Task.WhenAny(actionTask, cancelTcs.Task).ConfigureAwait(true);

            if (completedTask == cancelTcs.Task)
            {
                throw new OperationCanceledException(cts.Token);
            }

            await actionTask.ConfigureAwait(true);

            if (completionText != null)
            {
                lblStatus.Text = completionText;
            }
        }
        catch (OperationCanceledException)
        {
            lblStatus.Text = LocalizationService.GetString(ResourceKeys.MsgScanCancelled, "Operation cancelled.");
        }
        catch (Exception ex)
        {
            ShowAppError("Operation Failed", ex.Message);
        }
        finally
        {
            _activeOperationCts = null;
            btnCancelLoading.IsVisible = false;
            pnlLoadingOverlay.IsVisible = false;
            isLoading = false;
        }
    }

    internal void ShowAppError(string title, string message)
    {
        if (txtErrorTitle != null) txtErrorTitle.Text = title;
        if (txtErrorMessage != null) txtErrorMessage.Text = message;
        if (pnlErrorOverlay != null) pnlErrorOverlay.IsVisible = true;
    }

    private Task ExecuteWithLoadingAsync(string statusText, Func<Task> action, string? completionText = null)
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
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.A && rbViewGrid?.IsChecked == true)
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
            if (!isComparingRaw && slides != null && slides.SelectedIndex >= 0)
            {
                int sel = slides.SelectedIndex;
                var engine = ScanSessions[CurrentIndex].Activate();
                if (sel < engine.RawDetectedPhotos.Count)
                {
                    isComparingRaw = true;
                    var oldBmp = slides.Items[sel] as IDisposable;
                    slides.Items[sel] = MatBitmapConverter.ToAvaloniaBitmap(engine.RawDetectedPhotos[sel]);
                    slides.SelectedIndex = sel;
                    DeferDispose(oldBmp);
                }
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

            if (rbViewGrid?.IsChecked == true && lstGallery?.SelectedItems != null && lstGallery.SelectedItems.Count > 1)
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
                        if (slides != null) slides.Previous();
                    }
                    e.Handled = true;
                    return true;

                case Key.Right:
                    if (!isLoading && !_isNavigating)
                    {
                        FocusManager?.Focus(null);
                        if (slides != null) slides.Next();
                    }
                    e.Handled = true;
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Cancels any in-flight background lookahead and waits (up to 3 s) for it to fully
    /// release the <see cref="_lookaheadSemaphore"/>. This guarantees no background task
    /// is concurrently accessing a <see cref="PhotoCropper.Core.Engine.PhotoCropperEngine"/>
    /// when the caller is about to move or load sessions.
    /// </summary>
    private async Task DrainLookaheadAsync()
    {
        if (_lookaheadCts != null) await _lookaheadCts.CancelAsync();
        bool acquired = await _lookaheadSemaphore.WaitAsync(3000, CancellationToken.None);
        if (acquired) _lookaheadSemaphore.Release();
    }

    /// <summary>
    /// Moves to the next or previous scan and loads it into the GUI.
    /// Sets <see cref="_isNavigating"/> synchronously before any await so that
    /// re-entrant key events on the UI thread see the flag immediately and bail out.
    /// Then drains the lookahead to prevent concurrent <c>AutoTune</c> calls on the
    /// same engine from this task and the background lookahead task.
    /// </summary>
    private async Task NavigateScanAsync(bool forward)
    {
        _isNavigating = true;
        FocusManager?.Focus(null);
        await DrainLookaheadAsync();
        if (forward) _sessionManager.MoveNext(); else _sessionManager.MovePrevious();
        try
        {
            await LoadPhotosToGuiAsync();
        }
        finally
        {
            _isNavigating = false;
        }
    }

    private async Task NavigateToScanIndexAsync(int targetIndex)
    {
        if (targetIndex < 0 || targetIndex >= ScanSessions.Count || targetIndex == CurrentIndex) return;
        _isNavigating = true;
        FocusManager?.Focus(null);
        await DrainLookaheadAsync();
        _sessionManager.MoveTo(targetIndex);
        try
        {
            await LoadPhotosToGuiAsync();
        }
        finally
        {
            _isNavigating = false;
        }
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
            if (!isLoading && !_isNavigating && ScanSessions.Count > 0 && slides != null && slides.SelectedIndex >= 0)
            {
                int sel = slides.SelectedIndex;
                var engine = ScanSessions[CurrentIndex].Activate();
                if (sel < engine.DetectedPhotos.Count)
                {
                    var oldBmp = slides.Items[sel] as IDisposable;
                    slides.Items[sel] = MatBitmapConverter.ToAvaloniaBitmap(engine.DetectedPhotos[sel]);
                    slides.SelectedIndex = sel;
                    DeferDispose(oldBmp);
                }
            }
            e.Handled = true;
        }
    }

    internal void SetMainImage(Emgu.CV.Mat? mat)
    {
        if (img == null) return;

        var oldSource = img.Source as IDisposable;
        img.Source = mat != null ? MatBitmapConverter.ToAvaloniaBitmap(mat) : null;
        DeferDispose(oldSource);
        UpdateCropCanvasSize();
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
        settings.BackgroundTolerance = PhotoCropper.Core.Models.DetectionOptions.SensitivityToTolerance(sldSensitivity.Value);
        settings.ZoomLevel = sldZoom.Value;
        settings.MinAreaFactor = sldMinArea.Value;
        settings.MaxAreaFactor = sldMaxArea.Value;
        settings.CannyLowThreshold = sldEdge.Value;

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
    }
}
