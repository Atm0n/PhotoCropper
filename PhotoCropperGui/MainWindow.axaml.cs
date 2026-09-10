using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using PhotoCropper;

namespace PhotoCropperGui;

internal sealed partial class MainWindow : Window
{
    private int currentIndex;
    private readonly List<PhotoCropperEngine> OriginalPhotos = [];
    private bool isLoading;

    public MainWindow()
    {
        InitializeComponent();

        // Register key down handler in the Tunnel phase to prevent focused controls from hijacking keys
        AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);

        PopulateLanguageMenu();
        ApplySettingsToUi();
    }

    private void ApplySettingsToUi()
    {
        var settings = SettingsManager.Instance.Settings;
        sldSensitivity.Value = settings.BackgroundTolerance;
        sldZoom.Value = settings.ZoomLevel;
        sldMinArea.Value = settings.MinAreaFactor;
        sldMaxArea.Value = settings.MaxAreaFactor;
        sldEdge.Value = settings.CannyLowThreshold;
        tglAdvanced.IsChecked = settings.AdvancedVisible;

        if (cbFormat != null)
        {
            cbFormat.SelectedIndex = string.Equals(settings.PreferredFormat, "PNG", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }
        if (sldJpegQuality != null)
        {
            sldJpegQuality.Value = settings.JpegQuality;
        }
        if (txtOutputDir != null)
        {
            txtOutputDir.Text = settings.CustomOutputDirectory ?? "";
        }
        if (pnlJpegQuality != null)
        {
            pnlJpegQuality.IsVisible = !string.Equals(settings.PreferredFormat, "PNG", StringComparison.OrdinalIgnoreCase);
        }
    }

    private void PopulateLanguageMenu()
    {
        if (menuLanguage == null) return;

        var languages = LocalizationManager.GetAvailableLanguages();
        foreach (var lang in languages)
        {
            var item = new MenuItem
            {
                Header = lang.Name,
                Tag = lang.Code
            };
            item.Click += (s, e) =>
            {
                if (s is MenuItem mi && mi.Tag is string code)
                {
                    LocalizationManager.SetLanguage(code);
                }
            };
            menuLanguage.Items.Add(item);
        }
    }

    private async void BtnOpenFiles_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null)
        {
            return;
        }

        var storageProvider = topLevel.StorageProvider;
        var fileResult = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Application.Current?.FindResource("BtnOpenScans")?.ToString() ?? "Select Files",
            FileTypeFilter =
            [
                FilePickerFileTypes.ImageAll,
            ],
            AllowMultiple = true
        });

        if (fileResult.Count > 0)
        {
            foreach (var photo in OriginalPhotos)
            {
                photo.Dispose();
            }
            OriginalPhotos.Clear();
            currentIndex = 0;

            foreach (var file in fileResult)
            {
                var filePath = file.Path.LocalPath;
                var photo = new PhotoCropperEngine(filePath)
                {
                    // Synchronize new photos with the current slider values
                    BackgroundTolerance = sldSensitivity.Value,
                    MinAreaFactor = sldMinArea.Value / 100.0,
                    MaxAreaFactor = sldMaxArea.Value / 100.0,
                    CannyLowThreshold = sldEdge.Value,
                    CannyHighThreshold = sldEdge.Value * 2.5 // Traditional Canny ratio
                };
                OriginalPhotos.Add(photo);
            }
            await LoadPhotosToGuiAsync();
        }
    }

    private async Task LoadPhotosToGuiAsync()
    {
        if (OriginalPhotos.Count == 0) return;
        if (isLoading) return;
        isLoading = true;

        try
        {
            string fileName = Path.GetFileName(OriginalPhotos[currentIndex].OriginalFilePath);
            string processingMsg = Application.Current?.FindResource("ProcessingScan")?.ToString() ?? "Processing...";
            lblStatus.Text = $"{processingMsg} {fileName}";
            
            pnlLoadingOverlay.IsVisible = true;

            if (OriginalPhotos[currentIndex].DetectedPhotos.Count == 0)
            {
                await Task.Run(() => OriginalPhotos[currentIndex].DetectPhotos());
            }

            var mat = OriginalPhotos[currentIndex].OriginalWithDetected;

            img.Source = ConvertMatToAvaloniaBitmap(mat);

            string scanCounterFormat = Application.Current?.FindResource("ScanCounter")?.ToString() ?? "Scan {0} of {1}";
            txtFileCounter.Text = string.Format(scanCounterFormat, currentIndex + 1, OriginalPhotos.Count);
            lblStatus.Text = fileName;

            LoadCroppedPhotosToSlider();
            UpdatePhotoCounterLabel();

            if (btnResetBackground != null)
            {
                btnResetBackground.IsEnabled = OriginalPhotos[currentIndex].CustomBackgroundColorHsv != null;
            }
        }
        finally
        {
            pnlLoadingOverlay.IsVisible = false;
            isLoading = false;
        }
    }

    private void LoadCroppedPhotosToSlider()
    {
        slides.Items.Clear();

        foreach (var photo in OriginalPhotos[currentIndex].DetectedPhotos)
        {
            slides.Items.Add(ConvertMatToAvaloniaBitmap(photo));
        }

        if (slides.Items.Count > 0)
        {
            slides.SelectedIndex = 0;
        }
    }

    private async void BtnPrevScan_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (isLoading || OriginalPhotos.Count == 0) return;
        currentIndex = (currentIndex - 1 + OriginalPhotos.Count) % OriginalPhotos.Count;
        await LoadPhotosToGuiAsync();
    }

    private async void BtnNextScan_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (isLoading || OriginalPhotos.Count == 0) return;
        currentIndex = (currentIndex + 1) % OriginalPhotos.Count;
        await LoadPhotosToGuiAsync();
    }

    private void BtnSaveImages_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (isLoading || OriginalPhotos.Count == 0) return;

        var settings = SettingsManager.Instance.Settings;
        int totalSaved = 0;
        foreach (var originalPhoto in OriginalPhotos)
        {
            originalPhoto.SaveDetectedPhotos(settings.CustomOutputDirectory, settings.PreferredFormat, settings.JpegQuality);
            totalSaved += originalPhoto.DetectedPhotos.Count;
        }

        string msgFormat = Application.Current?.FindResource("MsgSaved")?.ToString() ?? "Successfully saved {0} photos.";
        lblStatus.Text = string.Format(msgFormat, totalSaved);
    }

    private void BtnDelete_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (isLoading) return;
        DeleteCurrentPhoto();
    }

    private void DeleteCurrentPhoto()
    {
        if (isLoading || OriginalPhotos.Count == 0 || slides == null) return;
        int photoIndex = slides.SelectedIndex;
        if (photoIndex < 0) return;

        OriginalPhotos[currentIndex].DeletePhoto(photoIndex);

        // Keep current position if possible, otherwise move back
        int nextIndex = photoIndex;
        if (nextIndex >= OriginalPhotos[currentIndex].DetectedPhotos.Count)
        {
            nextIndex = OriginalPhotos[currentIndex].DetectedPhotos.Count - 1;
        }

        LoadCroppedPhotosToSlider();
        slides.SelectedIndex = nextIndex;
        lblStatus.Text = Application.Current?.FindResource("MsgPhotoDeleted")?.ToString() ?? "Photo deleted.";
    }

    private async void BtnRotate_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (isLoading) return;
        await RotateCurrentPhotoAsync();
    }

    private async Task RotateCurrentPhotoAsync()
    {
        if (isLoading || OriginalPhotos.Count == 0) return;

        int photoIndex = slides.SelectedIndex;
        if (photoIndex < 0) return;

        pnlLoadingOverlay.IsVisible = true;
        lblStatus.Text = Application.Current?.FindResource("MsgRotating")?.ToString() ?? "Rotating...";

        await Task.Run(() => OriginalPhotos[currentIndex].RotatePhoto(photoIndex));

        int savedIndex = photoIndex;
        LoadCroppedPhotosToSlider();
        slides.SelectedIndex = savedIndex;

        pnlLoadingOverlay.IsVisible = false;
        lblStatus.Text = Application.Current?.FindResource("MsgPhotoRotated")?.ToString() ?? "Photo rotated.";
    }

    private void BtnHelp_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        pnlHelpOverlay.IsVisible = true;
    }

    private void BtnCloseHelp_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        pnlHelpOverlay.IsVisible = false;
    }

    private async void SldSensitivity_PointerCaptureLost(object? sender, Avalonia.Input.PointerCaptureLostEventArgs e)
    {
        if (isLoading || OriginalPhotos.Count == 0) return;

        // Update persistent settings
        var settings = SettingsManager.Instance.Settings;
        settings.BackgroundTolerance = sldSensitivity.Value;
        settings.MinAreaFactor = sldMinArea.Value;
        settings.MaxAreaFactor = sldMaxArea.Value;
        settings.CannyLowThreshold = sldEdge.Value;
        SettingsManager.Instance.Save();

        if (OriginalPhotos.Count > 0)
        {
            var photo = OriginalPhotos[currentIndex];
            photo.BackgroundTolerance = sldSensitivity.Value;
            photo.MinAreaFactor = sldMinArea.Value / 100.0;
            photo.MaxAreaFactor = sldMaxArea.Value / 100.0;
            photo.CannyLowThreshold = sldEdge.Value;
            photo.CannyHighThreshold = sldEdge.Value * 2.5;
            
            pnlLoadingOverlay.IsVisible = true;
            lblStatus.Text = Application.Current?.FindResource("MsgReprocessing")?.ToString() ?? "Reprocessing...";
            
            await Task.Run(() => photo.DetectPhotos());
            await LoadPhotosToGuiAsync();
            
            string msgFormat = Application.Current?.FindResource("MsgDetectionComplete")?.ToString() ?? "Detection complete. Found {0} photos.";
            lblStatus.Text = string.Format(msgFormat, photo.DetectedPhotos.Count);
        }
    }

    private void CbFormat_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (cbFormat == null || pnlJpegQuality == null) return;

        bool isJpeg = cbFormat.SelectedIndex == 0;
        pnlJpegQuality.IsVisible = isJpeg;

        var settings = SettingsManager.Instance.Settings;
        settings.PreferredFormat = isJpeg ? "JPEG" : "PNG";
        SettingsManager.Instance.Save();
    }

    private void SldJpegQuality_PointerCaptureLost(object? sender, Avalonia.Input.PointerCaptureLostEventArgs e)
    {
        if (sldJpegQuality == null) return;
        var settings = SettingsManager.Instance.Settings;
        settings.JpegQuality = (int)sldJpegQuality.Value;
        SettingsManager.Instance.Save();
    }

    private async void BtnBrowseDir_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;

        var folderResult = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Application.Current?.FindResource("LblOutputDir")?.ToString() ?? "Select Export Folder",
            AllowMultiple = false
        });

        if (folderResult != null && folderResult.Count > 0)
        {
            var path = folderResult[0].Path.LocalPath;
            txtOutputDir.Text = path;
            var settings = SettingsManager.Instance.Settings;
            settings.CustomOutputDirectory = path;
            SettingsManager.Instance.Save();
        }
    }

    private void BtnClearOutputDir_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (txtOutputDir == null) return;
        txtOutputDir.Text = "";
        var settings = SettingsManager.Instance.Settings;
        settings.CustomOutputDirectory = null;
        SettingsManager.Instance.Save();
    }

    private void Slides_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdatePhotoCounterLabel();
    }

    private void UpdatePhotoCounterLabel()
    {
        if (lblPhotoInfo == null || slides == null || OriginalPhotos == null) return;

        if (currentIndex < 0 || currentIndex >= OriginalPhotos.Count)
        {
            lblPhotoInfo.Text = "";
            return;
        }

        var currentPhoto = OriginalPhotos[currentIndex];
        if (currentPhoto == null || currentPhoto.DetectedPhotos == null || currentPhoto.DetectedPhotos.Count == 0)
        {
            lblPhotoInfo.Text = "";
            return;
        }

        int current = slides.SelectedIndex + 1;
        int total = currentPhoto.DetectedPhotos.Count;
        string format = Application.Current?.FindResource("PhotoCounter")?.ToString() ?? "PHOTO {0} OF {1}";
        lblPhotoInfo.Text = string.Format(format, current, total);
    }

    private void BtnPreviousCroppedImage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (isLoading) return;
        slides.Previous();
    }

    private void BtnNextCroppedImage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (isLoading) return;
        slides.Next();
    }

    private async void Window_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (isLoading)
        {
            e.Handled = true;
            return;
        }

        // 1. Modifier-based Shortcuts
        if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control) && e.Key == Avalonia.Input.Key.S)
        {
            BtnSaveImages_Click(null, new Avalonia.Interactivity.RoutedEventArgs());
            e.Handled = true;
            return;
        }

        // 2. Overlay Closures
        if (pnlHelpOverlay.IsVisible && e.Key == Avalonia.Input.Key.Escape)
        {
            pnlHelpOverlay.IsVisible = false;
            e.Handled = true;
            return;
        }

        // 3. Refinement Mode Logic
        if (isRefining)
        {
            switch (e.Key)
            {
                case Avalonia.Input.Key.Enter:
                case Avalonia.Input.Key.A:
                    AcceptRefine();
                    e.Handled = true;
                    break;
                case Avalonia.Input.Key.Back:
                case Avalonia.Input.Key.Escape:
                case Avalonia.Input.Key.C:
                    RejectRefine();
                    e.Handled = true;
                    break;
            }
            return;
        }

        if (OriginalPhotos.Count == 0) return;

        // 3. Main Navigation & Actions
        // Normalize keys to avoid duplicate switch cases (e.g. Add and OemPlus are often the same value)
        var key = e.Key;
        if (key == Avalonia.Input.Key.OemPlus) key = Avalonia.Input.Key.Add;
        if (key == Avalonia.Input.Key.OemMinus) key = Avalonia.Input.Key.Subtract;
        if (key == Avalonia.Input.Key.OemTilde || key == Avalonia.Input.Key.Oem3) key = Avalonia.Input.Key.N;

        switch (key)
        {
            case Avalonia.Input.Key.Up:
            case Avalonia.Input.Key.PageUp:
                currentIndex = (currentIndex - 1 + OriginalPhotos.Count) % OriginalPhotos.Count;
                await LoadPhotosToGuiAsync();
                e.Handled = true;
                break;

            case Avalonia.Input.Key.Down:
            case Avalonia.Input.Key.PageDown:
                currentIndex = (currentIndex + 1) % OriginalPhotos.Count;
                await LoadPhotosToGuiAsync();
                e.Handled = true;
                break;

            case Avalonia.Input.Key.Left:
                slides.Previous();
                e.Handled = true;
                break;

            case Avalonia.Input.Key.Right:
                slides.Next();
                e.Handled = true;
                break;

            case Avalonia.Input.Key.Add:
                sldZoom.Value = Math.Clamp(sldZoom.Value + 0.5, sldZoom.Minimum, sldZoom.Maximum);
                e.Handled = true;
                break;

            case Avalonia.Input.Key.Subtract:
                sldZoom.Value = Math.Clamp(sldZoom.Value - 0.5, sldZoom.Minimum, sldZoom.Maximum);
                e.Handled = true;
                break;

            case Avalonia.Input.Key.R:
                await RotateCurrentPhotoAsync();
                e.Handled = true;
                break;

            case Avalonia.Input.Key.X:
            case Avalonia.Input.Key.Delete:
                DeleteCurrentPhoto();
                e.Handled = true;
                break;

            case Avalonia.Input.Key.N:
                StartRefineMode();
                e.Handled = true;
                break;
        }
    }

    private void ScrollOriginal_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateCropCanvasSize();
    }

    private void SldZoom_PropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name == "Value")
        {
            UpdateCropCanvasSize();

            if (sldZoom != null)
            {
                SettingsManager.Instance.Settings.ZoomLevel = sldZoom.Value;
                SettingsManager.Instance.Save();
            }
        }
    }

    private void ScrollOriginal_PointerWheelChanged(object? sender, Avalonia.Input.PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
        {
            double oldZoom = sldZoom.Value;

            // 2. Calculate new zoom
            double delta = e.Delta.Y > 0 ? 1.1 : 0.9;
            double newZoom = Math.Clamp(oldZoom * delta, sldZoom.Minimum, sldZoom.Maximum);
            
            if (newZoom != oldZoom)
            {
                sldZoom.Value = newZoom;

                // 3. Adjust scroll offset to keep the cursor over the same part of the image
                // This prevents the "jumping" effect and keeps scrollbars stable relative to the cursor.
                double multiplier = newZoom / oldZoom;
                var newOffset = new Avalonia.Vector(
                    (scrollOriginal.Offset.X + e.GetPosition(scrollOriginal).X) * multiplier - e.GetPosition(scrollOriginal).X,
                    (scrollOriginal.Offset.Y + e.GetPosition(scrollOriginal).Y) * multiplier - e.GetPosition(scrollOriginal).Y
                );

                scrollOriginal.Offset = newOffset;
            }

            e.Handled = true;
        }
    }

    private void UpdateCropCanvasSize()
    {
        if (img == null || scrollOriginal == null || cnvCrop == null || pnlOriginal == null) return;

        // 1. Calculate the 'Base' size (Fit to Screen)
        double availableW = scrollOriginal.Viewport.Width - 20;
        double availableH = scrollOriginal.Viewport.Height - 20;

        if (availableW <= 0 || availableH <= 0) return;

        // Set the image base size to fit the viewport
        img.Width = availableW;
        img.Height = availableH;

        // 2. Tightly wrap the Panel around the zoomed image
        // LayoutTransformControl scales the image, but we want the Panel to match that size exactly
        // to prevent excessive scrolling space.
        double zoom = sldZoom.Value;
        
        // Use the actual rendered bounds of the image (Uniform stretch) multiplied by zoom
        // This ensures the container is exactly the size of the visible image.
        double zoomedW = img.Bounds.Width * zoom;
        double zoomedH = img.Bounds.Height * zoom;

        if (zoomedW > 0 && zoomedH > 0)
        {
            pnlOriginal.Width = zoomedW;
            pnlOriginal.Height = zoomedH;
        }

        // 3. Sync the cropping canvas
        cnvCrop.Width = pnlOriginal.Width;
        cnvCrop.Height = pnlOriginal.Height;
    }

    private Avalonia.Point startPoint;
    private bool isDragging;

    private void PnlOriginal_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (OriginalPhotos.Count == 0) return;

        if (tglColorPicker != null && tglColorPicker.IsChecked == true)
        {
            e.Handled = true;
            SampleBackgroundColorAtPointer(e.GetPosition(pnlOriginal));
            return;
        }

        UpdateCropCanvasSize();
        startPoint = e.GetPosition(pnlOriginal);
        isDragging = true;
        rectCrop.IsVisible = true;
        Canvas.SetLeft(rectCrop, startPoint.X);
        Canvas.SetTop(rectCrop, startPoint.Y);
        rectCrop.Width = 0;
        rectCrop.Height = 0;
    }

    private void PnlOriginal_PointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (!isDragging) return;
        var currentPoint = e.GetPosition(pnlOriginal);

        double x = Math.Min(startPoint.X, currentPoint.X);
        double y = Math.Min(startPoint.Y, currentPoint.Y);
        double w = Math.Abs(startPoint.X - currentPoint.X);
        double h = Math.Abs(startPoint.Y - currentPoint.Y);

        Canvas.SetLeft(rectCrop, x);
        Canvas.SetTop(rectCrop, y);
        rectCrop.Width = w;
        rectCrop.Height = h;
    }

    private void PnlOriginal_PointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (!isDragging) return;
        isDragging = false;
        rectCrop.IsVisible = false;

        var endPoint = e.GetPosition(pnlOriginal);
        var rect = new Avalonia.Rect(
            Math.Min(startPoint.X, endPoint.X),
            Math.Min(startPoint.Y, endPoint.Y),
            Math.Abs(startPoint.X - endPoint.X),
            Math.Abs(startPoint.Y - endPoint.Y)
        );

        if (rect.Width < 5 || rect.Height < 5) return;

        ApplyManualCrop(rect);
    }

    private async void ApplyManualCrop(Avalonia.Rect uiRect)
    {
        var photo = OriginalPhotos[currentIndex];

        // Map UI coordinates to actual Image pixels
        var imageRect = GetImageRectInsideControl();
        if (imageRect.Width <= 0 || imageRect.Height <= 0) return;

        // Coordinates are relative to pnlOriginal, which includes the zoom transform
        double scaleX = photo.Original.Width / imageRect.Width;
        double scaleY = photo.Original.Height / imageRect.Height;

        int x = (int)((uiRect.X - imageRect.X) * scaleX);
        int y = (int)((uiRect.Y - imageRect.Y) * scaleY);
        int w = (int)(uiRect.Width * scaleX);
        int h = (int)(uiRect.Height * scaleY);

        var rect = new System.Drawing.Rectangle(x, y, w, h);
        
        pnlLoadingOverlay.IsVisible = true;
        lblStatus.Text = Application.Current?.FindResource("MsgExtractingCrop")?.ToString() ?? "Extracting manual crop...";
        
        await Task.Run(() => photo.AddManualCrop(rect));

        LoadCroppedPhotosToSlider();
        slides.SelectedIndex = photo.DetectedPhotos.Count - 1;
        
        pnlLoadingOverlay.IsVisible = false;
        lblStatus.Text = Application.Current?.FindResource("MsgManualCropAdded")?.ToString() ?? "Manual crop added.";
    }

    private Avalonia.Rect GetImageRectInsideControl()
    {
        if (img == null || img.Source == null || pnlOriginal == null) return new Avalonia.Rect();

        // Manual calculation of the image boundaries inside the centered panel
        // img.Bounds gives the 'Fit' size, sldZoom.Value gives the scaling factor.
        double zoom = sldZoom.Value;
        double w = img.Bounds.Width * zoom;
        double h = img.Bounds.Height * zoom;

        // Since the LayoutTransformControl is centered in pnlOriginal
        double x = (pnlOriginal.Bounds.Width - w) / 2;
        double y = (pnlOriginal.Bounds.Height - h) / 2;

        return new Avalonia.Rect(x, y, w, h);
    }

    private bool isRefining;
    private System.Drawing.Rectangle currentRefineRect;
    private Avalonia.Point startRefinePoint;
    private bool isRefineDragging;

    private void BtnRefine_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        StartRefineMode();
    }

    private void StartRefineMode()
    {
        if (OriginalPhotos.Count == 0 || slides == null) return;
        int photoIndex = slides.SelectedIndex;
        if (photoIndex < 0) return;

        var photoCropper = OriginalPhotos[currentIndex];
        currentRefineRect = photoCropper.GetRefinedCropRect(photoIndex);

        if (currentRefineRect.IsEmpty || currentRefineRect.Width <= 10 || currentRefineRect.Height <= 10)
        {
            // If it failed to find a good auto-crop, default to full image so user can manually crop it.
            currentRefineRect = new System.Drawing.Rectangle(0, 0, photoCropper.DetectedPhotos[photoIndex].Width, photoCropper.DetectedPhotos[photoIndex].Height);
        }

        isRefining = true;
        pnlRefineOverlay.IsVisible = true;
        lblStatus.Text = Application.Current?.FindResource("MsgRefineModeHelp")?.ToString() ?? "Refinement mode active.";

        UpdateRefinePreview();
    }

    private void UpdateRefinePreview()
    {
        if (OriginalPhotos.Count == 0 || slides == null) return;
        int photoIndex = slides.SelectedIndex;
        if (photoIndex < 0) return;

        var photoCropper = OriginalPhotos[currentIndex];
        using Mat previewMat = photoCropper.DetectedPhotos[photoIndex].Clone();

        System.Drawing.Rectangle drawRect = currentRefineRect;
        int thickness = 8;
        drawRect.Inflate(-thickness / 2, -thickness / 2);

        CvInvoke.Rectangle(previewMat, drawRect, new MCvScalar(0, 0, 255), thickness);

        imgRefine.Source = ConvertMatToAvaloniaBitmap(previewMat);
    }

    private static Bitmap ConvertMatToAvaloniaBitmap(Mat mat)
    {
        // If already 4-channel BGRA (supports transparency), use directly
        if (mat.NumberOfChannels == 4)
        {
            return new Bitmap(
                Avalonia.Platform.PixelFormat.Bgra8888,
                Avalonia.Platform.AlphaFormat.Premul,
                mat.DataPointer,
                new Avalonia.PixelSize(mat.Width, mat.Height),
                new Avalonia.Vector(96, 96),
                mat.Step);
        }

        // 1. Convert BGR to BGRA (adds an alpha channel without swapping colors)
        using Mat bgraMat = new();
        CvInvoke.CvtColor(mat, bgraMat, ColorConversion.Bgr2Bgra);

        // 2. Create Avalonia Bitmap directly from the Mat's data pointer
        return new Bitmap(
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul,
            bgraMat.DataPointer,
            new Avalonia.PixelSize(bgraMat.Width, bgraMat.Height),
            new Avalonia.Vector(96, 96),
            bgraMat.Step);
    }
    private void PnlRefine_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (!isRefining) return;
        startRefinePoint = e.GetPosition(pnlRefineImage);
        isRefineDragging = true;
        rectRefineCrop.IsVisible = true;
        Canvas.SetLeft(rectRefineCrop, startRefinePoint.X);
        Canvas.SetTop(rectRefineCrop, startRefinePoint.Y);
        rectRefineCrop.Width = 0;
        rectRefineCrop.Height = 0;
    }

    private void PnlRefine_PointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (!isRefineDragging) return;
        var currentPoint = e.GetPosition(pnlRefineImage);

        double x = Math.Min(startRefinePoint.X, currentPoint.X);
        double y = Math.Min(startRefinePoint.Y, currentPoint.Y);
        double w = Math.Abs(startRefinePoint.X - currentPoint.X);
        double h = Math.Abs(startRefinePoint.Y - currentPoint.Y);

        Canvas.SetLeft(rectRefineCrop, x);
        Canvas.SetTop(rectRefineCrop, y);
        rectRefineCrop.Width = w;
        rectRefineCrop.Height = h;
    }

    private void PnlRefine_PointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (!isRefineDragging) return;
        isRefineDragging = false;
        rectRefineCrop.IsVisible = false;

        var endPoint = e.GetPosition(pnlRefineImage);
        var uiRect = new Avalonia.Rect(
            Math.Min(startRefinePoint.X, endPoint.X),
            Math.Min(startRefinePoint.Y, endPoint.Y),
            Math.Abs(startRefinePoint.X - endPoint.X),
            Math.Abs(startRefinePoint.Y - endPoint.Y)
        );

        if (uiRect.Width < 5 || uiRect.Height < 5) return;

        var imageRect = GetRefineImageRectInsideControl();
        if (imageRect.Width <= 0 || imageRect.Height <= 0) return;

        int photoIndex = slides.SelectedIndex;
        var photo = OriginalPhotos[currentIndex].DetectedPhotos[photoIndex];

        double scaleX = photo.Width / imageRect.Width;
        double scaleY = photo.Height / imageRect.Height;

        int x = (int)((uiRect.X - imageRect.X) * scaleX);
        int y = (int)((uiRect.Y - imageRect.Y) * scaleY);
        int w = (int)(uiRect.Width * scaleX);
        int h = (int)(uiRect.Height * scaleY);

        currentRefineRect = new System.Drawing.Rectangle(x, y, w, h);
        currentRefineRect.Intersect(new System.Drawing.Rectangle(System.Drawing.Point.Empty, photo.Size));

        UpdateRefinePreview();
    }

    private Avalonia.Rect GetRefineImageRectInsideControl()
    {
        if (imgRefine == null || imgRefine.Source == null) return new Avalonia.Rect();

        var controlSize = pnlRefineImage.Bounds.Size;
        var imageSize = imgRefine.Source.Size;

        double availableWidth = controlSize.Width - 40; // 20 margin each side
        double availableHeight = controlSize.Height - 40;

        double scale = Math.Min(availableWidth / imageSize.Width, availableHeight / imageSize.Height);

        double w = imageSize.Width * scale;
        double h = imageSize.Height * scale;
        double x = 20 + (availableWidth - w) / 2;
        double y = 20 + (availableHeight - h) / 2;

        return new Avalonia.Rect(x, y, w, h);
    }

    private void BtnAcceptRefine_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        AcceptRefine();
    }

    private void BtnRejectRefine_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        RejectRefine();
    }

    private async void AcceptRefine()
    {
        if (!isRefining) return;

        int photoIndex = slides.SelectedIndex;
        
        pnlLoadingOverlay.IsVisible = true;
        lblStatus.Text = Application.Current?.FindResource("MsgApplyingRefine")?.ToString() ?? "Applying refinement...";

        await Task.Run(() => OriginalPhotos[currentIndex].ApplyCropToPhoto(photoIndex, currentRefineRect));

        CloseRefineMode();

        int savedIndex = photoIndex;
        LoadCroppedPhotosToSlider();
        slides.SelectedIndex = savedIndex;
        
        pnlLoadingOverlay.IsVisible = false;
        lblStatus.Text = Application.Current?.FindResource("MsgRefineSuccess")?.ToString() ?? "Crop refined successfully.";
    }

    private void RejectRefine()
    {
        if (!isRefining) return;
        CloseRefineMode();
        lblStatus.Text = Application.Current?.FindResource("MsgRefineCancelled")?.ToString() ?? "Refinement cancelled.";
    }

    private void CloseRefineMode()
    {
        isRefining = false;
        pnlRefineOverlay.IsVisible = false;
        imgRefine.Source = null;
    }

    private async void BtnResetDefaults_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (isLoading) return;

        SettingsManager.Instance.ResetDetectionDefaults();
        
        // Apply settings back to UI (visual update)
        var settings = SettingsManager.Instance.Settings;
        sldSensitivity.Value = settings.BackgroundTolerance;
        sldMinArea.Value = settings.MinAreaFactor;
        sldMaxArea.Value = settings.MaxAreaFactor;
        sldEdge.Value = settings.CannyLowThreshold;

        // Re-process current scan if loaded
        if (OriginalPhotos.Count > 0)
        {
            var photo = OriginalPhotos[currentIndex];
            photo.BackgroundTolerance = settings.BackgroundTolerance;
            photo.MinAreaFactor = settings.MinAreaFactor / 100.0;
            photo.MaxAreaFactor = settings.MaxAreaFactor / 100.0;
            photo.CannyLowThreshold = settings.CannyLowThreshold;
            photo.CannyHighThreshold = settings.CannyLowThreshold * 2.5;

            pnlLoadingOverlay.IsVisible = true;
            lblStatus.Text = Application.Current?.FindResource("MsgReprocessing")?.ToString() ?? "Reprocessing...";

            await Task.Run(() => photo.DetectPhotos());
            await LoadPhotosToGuiAsync();

            string msgFormat = Application.Current?.FindResource("MsgDetectionComplete")?.ToString() ?? "Detection complete. Found {0} photos.";
            lblStatus.Text = string.Format(msgFormat, photo.DetectedPhotos.Count);
        }
    }

    private void TglColorPicker_Click(object? sender, RoutedEventArgs e)
    {
        if (pnlOriginal == null || tglColorPicker == null) return;
        pnlOriginal.Cursor = tglColorPicker.IsChecked == true 
            ? new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Cross) 
            : Avalonia.Input.Cursor.Default;
    }

    private async void SampleBackgroundColorAtPointer(Avalonia.Point uiPoint)
    {
        if (OriginalPhotos.Count == 0 || tglColorPicker == null || btnResetBackground == null) return;
        var photo = OriginalPhotos[currentIndex];

        var imageRect = GetImageRectInsideControl();
        if (imageRect.Width <= 0 || imageRect.Height <= 0) return;

        // Map display coordinate to original physical scan pixels
        double scaleX = photo.Original.Width / imageRect.Width;
        double scaleY = photo.Original.Height / imageRect.Height;

        int x = (int)((uiPoint.X - imageRect.X) * scaleX);
        int y = (int)((uiPoint.Y - imageRect.Y) * scaleY);

        // Reset cursor and toggle
        pnlOriginal.Cursor = Avalonia.Input.Cursor.Default;
        tglColorPicker.IsChecked = false;

        pnlLoadingOverlay.IsVisible = true;
        lblStatus.Text = Application.Current?.FindResource("MsgClickToSample")?.ToString() ?? "Sampling background color...";

        await Task.Run(() =>
        {
            photo.SetCustomBackgroundFromPixel(x, y);
            photo.DetectPhotos();
        });

        btnResetBackground.IsEnabled = true;

        await LoadPhotosToGuiAsync();

        string completeMsg = Application.Current?.FindResource("MsgBackgroundSampled")?.ToString() ?? "Custom background color applied.";
        lblStatus.Text = completeMsg;
    }

    private async void BtnResetBackground_Click(object? sender, RoutedEventArgs e)
    {
        if (OriginalPhotos.Count == 0 || btnResetBackground == null) return;
        var photo = OriginalPhotos[currentIndex];

        photo.CustomBackgroundColorHsv = null;
        btnResetBackground.IsEnabled = false;

        pnlLoadingOverlay.IsVisible = true;
        lblStatus.Text = Application.Current?.FindResource("MsgReprocessing")?.ToString() ?? "Reprocessing with automatic background...";

        await Task.Run(() => photo.DetectPhotos());
        await LoadPhotosToGuiAsync();

        lblStatus.Text = Application.Current?.FindResource("MsgDetectionComplete")?.ToString() ?? "Detection complete.";
    }

    protected override void OnClosed(System.EventArgs e)
    {
        // Capture final UI state to settings before exiting
        var settings = SettingsManager.Instance.Settings;
        settings.BackgroundTolerance = sldSensitivity.Value;
        settings.ZoomLevel = sldZoom.Value;
        settings.MinAreaFactor = sldMinArea.Value;
        settings.MaxAreaFactor = sldMaxArea.Value;
        settings.CannyLowThreshold = sldEdge.Value;
        settings.AdvancedVisible = tglAdvanced.IsChecked ?? false;

        if (cbFormat != null)
        {
            settings.PreferredFormat = cbFormat.SelectedIndex == 1 ? "PNG" : "JPEG";
        }
        if (sldJpegQuality != null)
        {
            settings.JpegQuality = (int)sldJpegQuality.Value;
        }
        if (txtOutputDir != null)
        {
            settings.CustomOutputDirectory = string.IsNullOrEmpty(txtOutputDir.Text) ? null : txtOutputDir.Text;
        }

        SettingsManager.Instance.Save();

        base.OnClosed(e);
        foreach (var photo in OriginalPhotos)
        {
            photo.Dispose();
        }
        OriginalPhotos.Clear();
    }
}
