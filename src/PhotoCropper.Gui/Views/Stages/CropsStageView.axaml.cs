using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using PhotoCropper.Core;
using PhotoCropper.Gui.Services;
using PhotoCropper.Gui.ViewModels;

namespace PhotoCropper.Gui.Views.Stages;

/// <summary>What the Crops view asks of the window: actions that open other UI or touch the project.</summary>
internal interface ICropsViewActions
{
    void StartRefine();
    Task OpenReviewAsync();
    Task ExportSinglePhotoAsync(int photoIndex);
    Task DeleteCurrentScanAsync();
}

/// <summary>
/// The Crops stage: the scan with its boxes (zoom, pointer handling, background colour picker) and the photo panel.
/// State and logic are in <see cref="CropsStageViewModel"/>; this maps the pointer to scan pixels, draws the selection
/// rectangle, keeps the image sized to the viewport and collapses the photo panel.
/// </summary>
internal sealed partial class CropsStageView : UserControl
{
    private Point _pressPoint;
    private BoxGesture _gesture;

    /// <summary>The scan currently shown, so editing boxes only repaints the overlay instead of re-uploading the scan.</summary>
    private OpenCvSharp.Mat? _shownScan;

    private bool _rightPanelCollapsed;
    private GridLength _savedRightColumnWidth = new(1, GridUnitType.Star);
    private double _savedRightColumnMinWidth = 550;

    public CropsStageView()
    {
        InitializeComponent();
    }

    public ICropsViewActions? Actions { get; set; }

    private CropsStageViewModel? ViewModel => DataContext as CropsStageViewModel;

    /// <summary>The "Box colour" menu on the scan toolbar; the window fills it (the main menu has the same one).</summary>
    public MenuItem DetectionColorMenu => menuDetectionColor;

    public double ZoomLevel
    {
        get => sldZoom.Value;
        set => sldZoom.Value = value;
    }

    // ---- scan display ----

    /// <summary>Shows a scan with its boxes drawn over it. The bitmap is only rebuilt when the scan itself changed.</summary>
    public void ShowScan(PhotoCropperEngine? engine)
    {
        var scan = engine?.Original;
        if (scan == null || scan.Empty())
        {
            SetScanImage(null);
            return;
        }

        if (!ReferenceEquals(scan, _shownScan)) SetScanImage(scan);
        ShowBoxes(engine!);
        btnResetBackground.IsEnabled = engine!.CustomBackgroundColorHsv != null;
    }

    private void SetScanImage(OpenCvSharp.Mat? mat)
    {
        var oldSource = img.Source as IDisposable;
        img.Source = mat != null ? MatBitmapConverter.ToAvaloniaBitmap(mat) : null;
        _shownScan = mat;
        DeferredDisposal.Later(oldSource);
        UpdateCanvasSize();
        if (mat == null) cropOverlay.Candidates = null;
    }

    private void ShowBoxes(PhotoCropperEngine engine)
    {
        cropOverlay.ScanSize = new Size(engine.Original.Width, engine.Original.Height);
        cropOverlay.OverlayScale = engine.OverlayScale;
        var bgr = engine.CurrentOptions.GetBoundingBoxColorBgr();
        cropOverlay.BoxColor = Color.FromRgb((byte)bgr.V2, (byte)bgr.V1, (byte)bgr.V0);
        cropOverlay.Candidates = engine.AcceptedCandidates;
        // The engine may have edited the same list instance in place; repaint either way.
        cropOverlay.InvalidateVisual();
    }

    /// <summary>Colours of the rectangle drawn for a manual crop and of the box-colour swatch.</summary>
    public void SetSelectionColor(Color stroke, Color fill)
    {
        rectCrop.Stroke = new SolidColorBrush(stroke);
        rectCrop.Fill = new SolidColorBrush(fill);
        rectCurrentDetectionColor.Fill = new SolidColorBrush(stroke);
    }

    /// <summary>Fits the scan into the viewport, then applies the zoom.</summary>
    private void UpdateCanvasSize()
    {
        double availableW = scrollOriginal.Viewport.Width - 20;
        double availableH = scrollOriginal.Viewport.Height - 20;
        if (availableW <= 0 || availableH <= 0) return;

        if (img.Source is Avalonia.Media.Imaging.Bitmap bmp && bmp.PixelSize.Width > 0 && bmp.PixelSize.Height > 0)
        {
            double scale = Math.Min(availableW / bmp.PixelSize.Width, availableH / bmp.PixelSize.Height);
            double fitW = Math.Max(10, bmp.PixelSize.Width * scale);
            double fitH = Math.Max(10, bmp.PixelSize.Height * scale);
            img.Width = fitW;
            img.Height = fitH;

            double zoom = sldZoom.Value;
            pnlOriginal.Width = cnvCrop.Width = fitW * zoom;
            pnlOriginal.Height = cnvCrop.Height = fitH * zoom;
        }
        else
        {
            img.Width = pnlOriginal.Width = cnvCrop.Width = availableW;
            img.Height = pnlOriginal.Height = cnvCrop.Height = availableH;
        }
    }

    /// <summary>Where the scan is drawn inside the pointer panel, for mapping the pointer to scan pixels.</summary>
    private Rect ImageRectInPanel()
    {
        if (img.Source == null) return default;

        double zoom = sldZoom.Value;
        double w = (double.IsNaN(img.Width) ? img.Bounds.Width : img.Width) * zoom;
        double h = (double.IsNaN(img.Height) ? img.Bounds.Height : img.Height) * zoom;
        double x = (pnlOriginal.Bounds.Width - w) / 2;
        double y = (pnlOriginal.Bounds.Height - h) / 2;
        return new Rect(Math.Max(0, x), Math.Max(0, y), Math.Max(1, w), Math.Max(1, h));
    }

    private static System.Drawing.Size ScanSize(PhotoCropperEngine engine) => new(engine.Original.Width, engine.Original.Height);

    private System.Drawing.Point ToScanPixel(Point uiPoint, PhotoCropperEngine engine) =>
        CoordinateMapper.MapUiPointToImagePixel(uiPoint, ImageRectInPanel(), ScanSize(engine));

    // ---- zoom ----

    private void ScrollOriginal_SizeChanged(object? sender, SizeChangedEventArgs e) => UpdateCanvasSize();

    private void SldZoom_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Avalonia.Controls.Primitives.RangeBase.ValueProperty) return;
        UpdateCanvasSize();
        SettingsManager.Instance.Settings.ZoomLevel = sldZoom.Value;
        SettingsManager.Instance.Save();
    }

    /// <summary>Ctrl + wheel zooms around the pointer.</summary>
    private void ScrollOriginal_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;

        double oldZoom = sldZoom.Value;
        double newZoom = Math.Clamp(oldZoom * (e.Delta.Y > 0 ? 1.1 : 0.9), sldZoom.Minimum, sldZoom.Maximum);
        if (Math.Abs(newZoom - oldZoom) > 0.0001)
        {
            sldZoom.Value = newZoom;
            double factor = newZoom / oldZoom;
            var pointer = e.GetPosition(scrollOriginal);
            scrollOriginal.Offset = new Vector(
                (scrollOriginal.Offset.X + pointer.X) * factor - pointer.X,
                (scrollOriginal.Offset.Y + pointer.Y) * factor - pointer.Y);
        }
        e.Handled = true;
    }

    // ---- pointer on the scan ----

    private void PnlOriginal_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is not { } vm || vm.ActivateCurrentScan() is not { } engine) return;

        if (tglColorPicker.IsChecked == true)
        {
            e.Handled = true;
            var pixel = ToScanPixel(e.GetPosition(pnlOriginal), engine);
            pnlOriginal.Cursor = Cursor.Default;
            tglColorPicker.IsChecked = false;
            _ = vm.UseBackgroundColorAtAsync(pixel).ContinueWith(t => System.Diagnostics.Trace.TraceError(t.Exception?.ToString()), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            return;
        }

        UpdateCanvasSize();
        _pressPoint = e.GetPosition(pnlOriginal);
        _gesture = vm.PressAt(ToScanPixel(_pressPoint, engine));
        if (_gesture is BoxGesture.Rotate or BoxGesture.Resize)
        {
            e.Handled = true;
            return;
        }
        if (_gesture != BoxGesture.Draw) return;

        rectCrop.IsVisible = true;
        Canvas.SetLeft(rectCrop, _pressPoint.X);
        Canvas.SetTop(rectCrop, _pressPoint.Y);
        rectCrop.Width = 0;
        rectCrop.Height = 0;
    }

    private void PnlOriginal_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (ViewModel is not { } vm) return;

        if (_gesture is BoxGesture.Rotate or BoxGesture.Resize)
        {
            if (vm.ActivateCurrentScan() is { } engine) vm.DragTo(ToScanPixel(e.GetPosition(pnlOriginal), engine));
            return;
        }

        if (_gesture != BoxGesture.Draw) return;
        var rect = CoordinateMapper.ComputeNormalizedRect(_pressPoint, e.GetPosition(pnlOriginal));
        Canvas.SetLeft(rectCrop, rect.X);
        Canvas.SetTop(rectCrop, rect.Y);
        rectCrop.Width = rect.Width;
        rectCrop.Height = rect.Height;
    }

    private void PnlOriginal_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var gesture = _gesture;
        _gesture = BoxGesture.None;
        if (ViewModel is not { } vm) return;

        var rect = CoordinateMapper.ComputeNormalizedRect(_pressPoint, e.GetPosition(pnlOriginal));
        bool wasTap = rect.Width < 5 && rect.Height < 5;

        if (gesture is BoxGesture.Rotate or BoxGesture.Resize)
        {
            vm.Release(wasTap);
            return;
        }

        if (gesture != BoxGesture.Draw) return;
        rectCrop.IsVisible = false;
        if (vm.ActivateCurrentScan() is not { } engine) return;

        var scanRect = CoordinateMapper.MapUiRectToImageRect(rect, ImageRectInPanel(), ScanSize(engine));
        if (rect.Width < 5 || rect.Height < 5)
        {
            vm.ClickAt(new System.Drawing.PointF(scanRect.X, scanRect.Y));
            return;
        }

        _ = vm.AddManualCropAsync(scanRect).ContinueWith(t => System.Diagnostics.Trace.TraceError(t.Exception?.ToString()), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    // ---- background colour ----

    private void TglColorPicker_Click(object? sender, RoutedEventArgs e) =>
        pnlOriginal.Cursor = tglColorPicker.IsChecked == true ? new Cursor(StandardCursorType.Cross) : Cursor.Default;

    private async void BtnResetBackground_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm) await vm.ResetBackgroundColorAsync();
    }

    // ---- scan and photo actions ----

    private async void BtnDeleteScan_Click(object? sender, RoutedEventArgs e)
    {
        if (Actions != null) await Actions.DeleteCurrentScanAsync();
    }

    private void BtnDelete_Click(object? sender, RoutedEventArgs e) => ViewModel?.DeleteSelectedPhotos();

    private async void BtnRotate_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm) await vm.RotateSelectedPhotosAsync();
    }

    private void BtnRefine_Click(object? sender, RoutedEventArgs e) => Actions?.StartRefine();

    private async void BtnReviewPhotos_Click(object? sender, RoutedEventArgs e)
    {
        if (Actions != null) await Actions.OpenReviewAsync();
    }

    private async void ContextMenu_RotateCw_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        SelectContextPhoto(sender);
        await vm.RotateSelectedPhotosAsync();
    }

    private async void ContextMenu_RotateCcw_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        SelectContextPhoto(sender);
        await vm.RotateSelectedPhotosCounterClockwiseAsync();
    }

    private void ContextMenu_Refine_Click(object? sender, RoutedEventArgs e)
    {
        SelectContextPhoto(sender);
        Actions?.StartRefine();
    }

    private void ContextMenu_Delete_Click(object? sender, RoutedEventArgs e)
    {
        SelectContextPhoto(sender);
        ViewModel?.DeleteSelectedPhotos();
    }

    private async void ContextMenu_ExportSingle_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm || Actions == null) return;
        SelectContextPhoto(sender);
        if (vm.SelectedPhotoIndex >= 0 && vm.SelectedPhotoIndex < vm.Photos.Count)
        {
            await Actions.ExportSinglePhotoAsync(vm.Photos[vm.SelectedPhotoIndex].Index);
        }
    }

    /// <summary>A context menu acts on the photo it was opened on, unless that photo is part of the grid's selection.</summary>
    private void SelectContextPhoto(object? sender)
    {
        if (sender is MenuItem { DataContext: CropPhotoItem item }) ViewModel?.EnsureSelected(item);
    }

    // ---- photo panel ----

    private void BtnToggleRightPanel_Click(object? sender, RoutedEventArgs e) => ToggleRightPanel();

    /// <summary>Collapses the photo panel to a thin strip (or restores it) to give the scan more room.</summary>
    public void ToggleRightPanel()
    {
        _rightPanelCollapsed = !_rightPanelCollapsed;
        var colSplitter = grdMainWorkspace.ColumnDefinitions[1];
        var colRight = grdMainWorkspace.ColumnDefinitions[2];

        if (_rightPanelCollapsed)
        {
            _savedRightColumnWidth = colRight.Width;
            _savedRightColumnMinWidth = colRight.MinWidth;
            colRight.MinWidth = 0;
            colRight.Width = new GridLength(32);
            colSplitter.Width = new GridLength(0);
        }
        else
        {
            colRight.MinWidth = _savedRightColumnMinWidth;
            colRight.Width = _savedRightColumnWidth;
            colSplitter.Width = GridLength.Auto;
        }

        brdRightPanelContent.IsVisible = !_rightPanelCollapsed;
        pnlRightPanelHeader.IsVisible = !_rightPanelCollapsed;
        pnlGallerySelection.IsVisible = !_rightPanelCollapsed;
        txtToggleRightPanelIcon.Text = _rightPanelCollapsed ? "▶" : "◀";
    }
}
