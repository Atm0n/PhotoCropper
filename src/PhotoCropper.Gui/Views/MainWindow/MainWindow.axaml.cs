using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PhotoCropper.Core.Scanning;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Models;
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

        // Register key handlers in Tunnel phase
        AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, Window_KeyUp, RoutingStrategies.Tunnel);

        // Register Drag & Drop event handlers
        AddHandler(DragDrop.DragOverEvent, Window_DragOver);
        AddHandler(DragDrop.DropEvent, Window_Drop);

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
    private void BtnToggleAdvanced_Click(object? sender, RoutedEventArgs e)
    {
        if (tglAdvanced != null)
        {
            tglAdvanced.IsChecked = !tglAdvanced.IsChecked;
        }
    }

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

        // Prevent keyboard shortcuts from stealing input when typing in a TextBox (e.g. naming pattern, year, description)
        if (IsTextInputActive(FocusManager?.GetFocusedElement(), e.Source))
        {
            if (e.Key == Key.Escape || e.Key == Key.Enter)
            {
                FocusManager?.Focus(null);
                e.Handled = true;
                return;
            }

            // Still allow global file operations with Ctrl modifier
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                if (e.Key == Key.S)
                {
                    BtnSaveImages_Click(null, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.O)
                {
                    BtnOpenFiles_Click(null, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                }
            }

            // Do not handle; let the focused TextBox receive spaces, letters, arrows, backspace, delete, Ctrl+Z/Y/A/C/V/X
            return;
        }

        // Toggle hidden menu bar with Alt key or F10
        if (e.Key == Key.LeftAlt || e.Key == Key.RightAlt || e.Key == Key.F10)
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                ToggleMenuBar();
                e.Handled = true;
                return;
            }
        }

        if (pnlMenuBar != null && pnlMenuBar.IsVisible && e.Key == Key.Escape)
        {
            pnlMenuBar.IsVisible = false;
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.S)
            {
                BtnSaveImages_Click(null, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
            if (e.Key == Key.O)
            {
                BtnOpenFiles_Click(null, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Z)
            {
                await PerformUndoAsync();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Y)
            {
                await PerformRedoAsync();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.A && rbViewGrid?.IsChecked == true)
            {
                SelectAllGalleryPhotos();
                e.Handled = true;
                return;
            }
        }

        if (pnlUnsavedOverlay != null && pnlUnsavedOverlay.IsVisible && e.Key == Key.Escape)
        {
            pnlUnsavedOverlay.IsVisible = false;
            e.Handled = true;
            return;
        }

        if (pnlErrorOverlay.IsVisible && e.Key == Key.Escape)
        {
            pnlErrorOverlay.IsVisible = false;
            e.Handled = true;
            return;
        }

        if (pnlLoadingOverlay.IsVisible && btnCancelLoading.IsVisible && e.Key == Key.Escape)
        {
            btnCancelLoading.IsEnabled = false;
            txtLoadingText.Text = LocalizationService.GetString(ResourceKeys.TxtCancelling, "Cancelling...");
            _activeOperationCts?.CancelAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F1)
        {
            ShowHelpWindow();
            e.Handled = true;
            return;
        }

        if (rbViewGrid?.IsChecked == true && lstGallery?.SelectedItems != null && lstGallery.SelectedItems.Count > 1 && e.Key == Key.Escape)
        {
            ClearGallerySelection();
            e.Handled = true;
            return;
        }

        if (isRefining)
        {
            switch (e.Key)
            {
                case Key.Enter:
                case Key.A:
                    AcceptRefine();
                    e.Handled = true;
                    break;
                case Key.Back:
                case Key.Escape:
                case Key.C:
                    RejectRefine();
                    e.Handled = true;
                    break;
            }
            return;
        }

        if (e.Key == Key.F5)
        {
            BtnScan_Click(null, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (ScanSessions.Count == 0) return;

        if (e.Key == Key.Delete && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            await DeleteCurrentScanAsync();
            e.Handled = true;
            return;
        }

        var key = e.Key;
        if (key == Key.OemPlus) key = Key.Add;
        if (key == Key.OemMinus) key = Key.Subtract;
        if (key == Key.OemTilde || key == Key.Oem3) key = Key.N;

        switch (key)
        {
            case Key.Up:
            case Key.PageUp:
                FocusManager?.Focus(null);
                _sessionManager.MovePrevious();
                await LoadPhotosToGuiAsync();
                e.Handled = true;
                break;

            case Key.Down:
            case Key.PageDown:
                FocusManager?.Focus(null);
                _sessionManager.MoveNext();
                await LoadPhotosToGuiAsync();
                e.Handled = true;
                break;

            case Key.Left:
                FocusManager?.Focus(null);
                if (slides != null) slides.Previous();
                e.Handled = true;
                break;

            case Key.Right:
                FocusManager?.Focus(null);
                if (slides != null) slides.Next();
                e.Handled = true;
                break;

            case Key.R:
                FocusManager?.Focus(null);
                await RotateSelectedPhotosAsync();
                e.Handled = true;
                break;

            case Key.X:
            case Key.Delete:
                FocusManager?.Focus(null);
                DeleteSelectedPhotos();
                e.Handled = true;
                break;

            case Key.Space:
            case Key.B:
                if (!isComparingRaw && ScanSessions.Count > 0 && slides != null && slides.SelectedIndex >= 0)
                {
                    int sel = slides.SelectedIndex;
                    var engine = ScanSessions[CurrentIndex].Activate();
                    if (sel < engine.RawDetectedPhotos.Count)
                    {
                        isComparingRaw = true;
                        var oldBmp = slides.Items[sel] as IDisposable;
                        slides.Items[sel] = MatBitmapConverter.ToAvaloniaBitmap(engine.RawDetectedPhotos[sel]);
                        oldBmp?.Dispose();
                        slides.SelectedIndex = sel;
                    }
                }
                e.Handled = true;
                break;
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
            if (ScanSessions.Count > 0 && slides != null && slides.SelectedIndex >= 0)
            {
                int sel = slides.SelectedIndex;
                var engine = ScanSessions[CurrentIndex].Activate();
                if (sel < engine.DetectedPhotos.Count)
                {
                    var oldBmp = slides.Items[sel] as IDisposable;
                    slides.Items[sel] = MatBitmapConverter.ToAvaloniaBitmap(engine.DetectedPhotos[sel]);
                    oldBmp?.Dispose();
                    slides.SelectedIndex = sel;
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
        oldSource?.Dispose();
        UpdateCropCanvasSize();
    }

    internal void SetRefineImage(Emgu.CV.Mat? mat)
    {
        if (imgRefine == null) return;

        var oldSource = imgRefine.Source as IDisposable;
        imgRefine.Source = mat != null ? MatBitmapConverter.ToAvaloniaBitmap(mat) : null;
        oldSource?.Dispose();
    }

    protected override void OnClosed(EventArgs e)
    {
        var settings = SettingsManager.Instance.Settings;
        settings.BackgroundTolerance = PhotoCropper.Core.Models.DetectionOptions.SensitivityToTolerance(sldSensitivity.Value);
        settings.ZoomLevel = sldZoom.Value;
        settings.MinAreaFactor = sldMinArea.Value;
        settings.MaxAreaFactor = sldMaxArea.Value;
        settings.CannyLowThreshold = sldEdge.Value;
        settings.AdvancedVisible = tglAdvanced.IsChecked ?? false;

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
