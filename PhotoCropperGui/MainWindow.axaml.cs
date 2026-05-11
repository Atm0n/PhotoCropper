using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using SkiaSharp;
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
                    // Synchronize new photos with the current slider value
                    BackgroundTolerance = sldSensitivity.Value
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

        using var mat = OriginalPhotos[currentIndex].OriginalWithDetected;

        using var systemBitmap = EmguMatToSkia(mat);

        img.Source = ConvertToAvaloniaBitmap(systemBitmap);

        txtFileCounter.Text = $"Scan {currentIndex + 1} of {OriginalPhotos.Count}";
        lblStatus.Text = $"Loaded {fileName}";

        LoadCroppedPhotosToSlider();
        UpdatePhotoCounterLabel();

        pnlLoadingOverlay.IsVisible = false;
    }

    private void LoadCroppedPhotosToSlider()
    {
        slides.Items.Clear();

        foreach (var photo in OriginalPhotos[currentIndex].DetectedPhotos)
        {
            using var systemBitmap = EmguMatToSkia(photo);
            slides.Items.Add(ConvertToAvaloniaBitmap(systemBitmap));
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

    private void BtnRotate_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        RotateCurrentPhoto();
    }

    private void RotateCurrentPhoto()
    {
        if (OriginalPhotos.Count == 0) return;

        int photoIndex = slides.SelectedIndex;
        if (photoIndex < 0) return;

        OriginalPhotos[currentIndex].RotatePhoto(photoIndex);

        // Save current index to restore it after reloading
        int savedIndex = photoIndex;
        LoadCroppedPhotosToSlider();
        slides.SelectedIndex = savedIndex;
    }

    #endregion

    #region Sliders & Navigation

    private async void SldSensitivity_PointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (OriginalPhotos.Count > 0)
        {
            OriginalPhotos[currentIndex].BackgroundTolerance = sldSensitivity.Value;
            // Since DetectPhotos is synchronous in PhotoCropper.cs, we run it in Task.Run here if it takes too long,
            // but LoadPhotosToGuiAsync already does this if DetectedPhotos.Count == 0. 
            // Wait, DetectPhotos resets the state, so Count becomes 0.
            OriginalPhotos[currentIndex].DetectPhotos(); // We should move this to the background too!
            await LoadPhotosToGuiAsync();
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
                RotateCurrentPhoto();
                break;
            case Avalonia.Input.Key.X:
                DeleteCurrentPhoto();
                break;
        }
    }

    #endregion

    #region Manual Crop on Original

    private Avalonia.Point startPoint;
    private bool isDragging = false;

    private void PnlOriginal_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (OriginalPhotos.Count == 0) return;
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

    private void ApplyManualCrop(Avalonia.Rect uiRect)
    {
        var photo = OriginalPhotos[currentIndex];

        // Map UI coordinates to actual Image pixels
        var imageRect = GetImageRectInsideControl();
        if (imageRect.Width <= 0 || imageRect.Height <= 0) return;

        double scaleX = photo.Original.Width / imageRect.Width;
        double scaleY = photo.Original.Height / imageRect.Height;

        int x = (int)((uiRect.X - imageRect.X) * scaleX);
        int y = (int)((uiRect.Y - imageRect.Y) * scaleY);
        int w = (int)(uiRect.Width * scaleX);
        int h = (int)(uiRect.Height * scaleY);

        photo.AddManualCrop(new System.Drawing.Rectangle(x, y, w, h));
        LoadCroppedPhotosToSlider();
        slides.SelectedIndex = photo.DetectedPhotos.Count - 1;
        lblStatus.Text = "Manual crop added.";
    }

    private Avalonia.Rect GetImageRectInsideControl()
    {
        if (img == null || img.Source == null) return new Avalonia.Rect();

        var controlSize = pnlOriginal.Bounds.Size;
        var imageSize = img.Source.Size;

        double scale = Math.Min(controlSize.Width / imageSize.Width, controlSize.Height / imageSize.Height);

        // Add 10px margin from XAML
        double w = imageSize.Width * scale;
        double h = imageSize.Height * scale;
        double x = (controlSize.Width - w) / 2;
        double y = (controlSize.Height - h) / 2;

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
        Mat previewMat = photoCropper.DetectedPhotos[photoIndex].Clone();

        System.Drawing.Rectangle drawRect = currentRefineRect;
        int thickness = 8;
        drawRect.Inflate(-thickness / 2, -thickness / 2);

        CvInvoke.Rectangle(previewMat, drawRect, new MCvScalar(0, 0, 255), thickness);

        using var systemBitmap = EmguMatToSkia(previewMat);
        imgRefine.Source = ConvertToAvaloniaBitmap(systemBitmap);
        previewMat.Dispose();
    }
    public static SKBitmap EmguMatToSkia(Mat mat)
    {
        // 1. Ensure the image is in a format Skia understands (BGRA is standard)
        // We create a temporary Mat for the conversion
        using Mat bgraMat = new();
        CvInvoke.CvtColor(mat, bgraMat, ColorConversion.Bgr2Bgra);

        // 2. Define the Skia Image Info
        // Note: Emgu.CV Mat.Step is the 'RowBytes' in Skia terms
        var info = new SKImageInfo(
            bgraMat.Width,
            bgraMat.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul);

        // 3. Create the SKBitmap and set the pixels directly from the Mat's data pointer
        var bitmap = new SKBitmap();
        bitmap.InstallPixels(info, bgraMat.DataPointer, bgraMat.Step);

        // 4. IMPORTANT: Since InstallPixels uses the Mat's memory, 
        // we must create a full copy if the 'bgraMat' is about to be disposed.
        return bitmap.Copy();
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

    private void AcceptRefine()
    {
        if (!isRefining) return;

        int photoIndex = slides.SelectedIndex;
        OriginalPhotos[currentIndex].ApplyCropToPhoto(photoIndex, currentRefineRect);

        CloseRefineMode();

        int savedIndex = photoIndex;
        LoadCroppedPhotosToSlider();
        slides.SelectedIndex = savedIndex;
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

    private static Bitmap ConvertToAvaloniaBitmap(SKBitmap systemBitmap)
    {
        using MemoryStream memoryStream = new();
        systemBitmap.Encode(memoryStream, SKEncodedImageFormat.Png, 100);
        memoryStream.Seek(0, SeekOrigin.Begin);
        return new Bitmap(memoryStream);

    }

    #endregion
}
