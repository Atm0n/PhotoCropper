using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PhotoCropperGui;

public partial class MainWindow : Window
{
    private int currentIndex = 0;
    private readonly List<PhotoCropper.PhotoCropper> OriginalPhotos = [];

    public MainWindow()
    {
        InitializeComponent();
    }

    #region UI Initialization & File Handling

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
            Title = "Select Files",
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
                var photo = new PhotoCropper.PhotoCropper(filePath)
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

        string fileName = Path.GetFileName(OriginalPhotos[currentIndex].OriginalFilePath);
        lblStatus.Text = $"Processing: {fileName}...";
        
        pnlLoadingOverlay.IsVisible = true;

        if (OriginalPhotos[currentIndex].DetectedPhotos.Count == 0)
        {
            await Task.Run(() => OriginalPhotos[currentIndex].DetectPhotos());
        }

        var mat = OriginalPhotos[currentIndex].OriginalWithDetected;

        img.Source = ConvertMatToAvaloniaBitmap(mat);

        txtFileCounter.Text = $"Scan {currentIndex + 1} of {OriginalPhotos.Count}";
        lblStatus.Text = $"Loaded {fileName}";

        LoadCroppedPhotosToSlider();
        UpdatePhotoCounterLabel();

        pnlLoadingOverlay.IsVisible = false;
    }

    private void LoadCroppedPhotosToSlider()
    {
        // Dispose old bitmaps to prevent memory leaks
        foreach (var item in slides.Items)
        {
            if (item is Bitmap bmp)
            {
                // Avalonia Bitmaps don't have a public Dispose, but they wrap native resources.
                // In modern Avalonia, they are cleaned up by GC, but we can help by clearing references.
            }
        }
        
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

    #endregion

    #region Actions (Rotate, Delete, Save)

    private void BtnSaveImages_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (OriginalPhotos.Count == 0) return;

        int totalSaved = 0;
        foreach (var originalPhoto in OriginalPhotos)
        {
            originalPhoto.SaveDetectedPhotos();
            totalSaved += originalPhoto.DetectedPhotos.Count;
        }

        lblStatus.Text = $"Successfully saved {totalSaved} photos to 'cropped' folders.";
    }

    private void BtnDelete_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DeleteCurrentPhoto();
    }

    private void DeleteCurrentPhoto()
    {
        if (OriginalPhotos.Count == 0 || slides == null) return;
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
        lblStatus.Text = "Photo deleted.";
    }

    private async void BtnRotate_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await RotateCurrentPhotoAsync();
    }

    private async Task RotateCurrentPhotoAsync()
    {
        if (OriginalPhotos.Count == 0) return;

        int photoIndex = slides.SelectedIndex;
        if (photoIndex < 0) return;

        pnlLoadingOverlay.IsVisible = true;
        lblStatus.Text = "Rotating photo...";

        await Task.Run(() => OriginalPhotos[currentIndex].RotatePhoto(photoIndex));

        int savedIndex = photoIndex;
        LoadCroppedPhotosToSlider();
        slides.SelectedIndex = savedIndex;

        pnlLoadingOverlay.IsVisible = false;
        lblStatus.Text = "Photo rotated.";
    }

    #endregion

    #region Sliders & Navigation

    private async void SldSensitivity_PointerCaptureLost(object? sender, Avalonia.Input.PointerCaptureLostEventArgs e)
    {
        if (OriginalPhotos.Count > 0)
        {
            var photo = OriginalPhotos[currentIndex];
            photo.BackgroundTolerance = sldSensitivity.Value;
            photo.MinAreaFactor = sldMinArea.Value / 100.0;
            photo.MaxAreaFactor = sldMaxArea.Value / 100.0;
            photo.CannyLowThreshold = sldEdge.Value;
            photo.CannyHighThreshold = sldEdge.Value * 2.5;
            
            pnlLoadingOverlay.IsVisible = true;
            lblStatus.Text = "Reprocessing scan with new settings...";
            
            await Task.Run(() => photo.DetectPhotos());
            await LoadPhotosToGuiAsync();
            
            lblStatus.Text = $"Detection complete. Found {photo.DetectedPhotos.Count} photos.";
        }
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
        lblPhotoInfo.Text = $"PHOTO {current} OF {total}";
    }

    private void BtnPreviousCroppedImage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        slides.Previous();
    }

    private void BtnNextCroppedImage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        slides.Next();
    }

    private async void Window_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (isRefining)
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                AcceptRefine();
            }
            else if (e.Key == Avalonia.Input.Key.Back || e.Key == Avalonia.Input.Key.Escape)
            {
                RejectRefine();
            }
            return;
        }

        if (OriginalPhotos.Count == 0) return;

        // Catch N or OemTilde (usually Ñ on Spanish keyboards)
        if (e.Key == Avalonia.Input.Key.N || e.Key == Avalonia.Input.Key.OemTilde || e.Key == Avalonia.Input.Key.Oem3)
        {
            StartRefineMode();
            return;
        }

        switch (e.Key)
        {
            case Avalonia.Input.Key.Left:
                slides.Previous();
                break;
            case Avalonia.Input.Key.Right:
                slides.Next();
                break;
            case Avalonia.Input.Key.Up:
                currentIndex = (currentIndex + 1) % OriginalPhotos.Count;
                await LoadPhotosToGuiAsync();
                break;
            case Avalonia.Input.Key.Down:
                currentIndex = (currentIndex - 1 + OriginalPhotos.Count) % OriginalPhotos.Count;
                await LoadPhotosToGuiAsync();
                break;
            case Avalonia.Input.Key.R:
                await RotateCurrentPhotoAsync();
                break;
            case Avalonia.Input.Key.X:
                DeleteCurrentPhoto();
                break;
        }
    }

    #endregion

    private void ScrollOriginal_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateCropCanvasSize();
    }

    private void SldZoom_PropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name == "Value")
        {
            UpdateCropCanvasSize();
        }
    }

    private void ScrollOriginal_PointerWheelChanged(object? sender, Avalonia.Input.PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
        {
            // 1. Capture relative position before zoom
            var relativePos = e.GetPosition(pnlOriginal);
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

    #region Manual Crop on Original

    private Avalonia.Point startPoint;
    private bool isDragging = false;

    private void PnlOriginal_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (OriginalPhotos.Count == 0) return;
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
        lblStatus.Text = "Extracting manual crop...";
        
        await Task.Run(() => photo.AddManualCrop(rect));

        LoadCroppedPhotosToSlider();
        slides.SelectedIndex = photo.DetectedPhotos.Count - 1;
        
        pnlLoadingOverlay.IsVisible = false;
        lblStatus.Text = "Manual crop added.";
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

    #endregion

    #region Interactive Refinement Overlay

    private bool isRefining = false;
    private System.Drawing.Rectangle currentRefineRect;
    private Avalonia.Point startRefinePoint;
    private bool isRefineDragging = false;

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
        lblStatus.Text = "Refinement mode: Draw to manual crop, Enter to Accept, Backspace to Reject.";

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
        // 1. Convert BGR to BGRA (adds an alpha channel without swapping colors)
        // This is often more efficient and avoids R/B swap confusion
        using Mat bgraMat = new();
        CvInvoke.CvtColor(mat, bgraMat, ColorConversion.Bgr2Bgra);

        // 2. Create Avalonia Bitmap directly from the Mat's data pointer
        // We use Bgra8888 which matches the output of Bgr2Bgra
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
        lblStatus.Text = "Applying refinement...";

        await Task.Run(() => OriginalPhotos[currentIndex].ApplyCropToPhoto(photoIndex, currentRefineRect));

        CloseRefineMode();

        int savedIndex = photoIndex;
        LoadCroppedPhotosToSlider();
        slides.SelectedIndex = savedIndex;
        
        pnlLoadingOverlay.IsVisible = false;
        lblStatus.Text = "Crop refined successfully.";
    }

    private void RejectRefine()
    {
        if (!isRefining) return;
        CloseRefineMode();
        lblStatus.Text = "Refinement cancelled.";
    }

    private void CloseRefineMode()
    {
        isRefining = false;
        pnlRefineOverlay.IsVisible = false;
        imgRefine.Source = null;
    }

    #endregion

    #region Utility Methods

    protected override void OnClosed(System.EventArgs e)
    {
        base.OnClosed(e);
        foreach (var photo in OriginalPhotos)
        {
            photo.Dispose();
        }
        OriginalPhotos.Clear();
    }

    #endregion
}
