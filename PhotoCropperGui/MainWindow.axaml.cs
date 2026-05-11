using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Emgu.CV;
using System;
using System.Collections.Generic;
using System.IO;

namespace PhotoCropperGui;

public partial class MainWindow : Window
//TODO: improve GUI and finish save feature
//TODO: some way of discarding photos
//TODO: configurable values
//TODO: configure paths for generated files

{
    private int currentIndex = 0;
    private readonly List<PhotoCropper.PhotoCropper> OriginalPhotos = [];
    public MainWindow()
    {
        InitializeComponent();
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
            LoadPhotosToGui();
        }

    }

    private void LoadPhotosToGui()
    {
        if (OriginalPhotos.Count == 0) return;

        string fileName = Path.GetFileName(OriginalPhotos[currentIndex].OriginalFilePath);
        lblStatus.Text = $"Processing: {fileName}...";
        
        if (OriginalPhotos[currentIndex].DetectedPhotos.Count == 0)
        {
            OriginalPhotos[currentIndex].DetectPhotos();
        }

        using var bitmap = OriginalPhotos[currentIndex].OriginalWithDetected.ToBitmap();
        img.Source = ConvertToAvaloniaBitmap(bitmap);

        txtFileCounter.Text = $"Scan {currentIndex + 1} of {OriginalPhotos.Count}";
        lblStatus.Text = $"Loaded {fileName}";
        
        LoadCroppedPhotosToSlider();
        UpdatePhotoCounterLabel();
    }

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

    private Avalonia.Point startPoint;
    private bool isDragging = false;

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

    private void Slides_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdatePhotoCounterLabel();
    }

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

    private void SldSensitivity_PointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (OriginalPhotos.Count > 0)
        {
            OriginalPhotos[currentIndex].BackgroundTolerance = sldSensitivity.Value;
            OriginalPhotos[currentIndex].DetectPhotos();
            LoadPhotosToGui();
        }
    }

    private void Window_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (OriginalPhotos.Count == 0) return;

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
                LoadPhotosToGui();
                break;
            case Avalonia.Input.Key.Down:
                currentIndex = (currentIndex - 1 + OriginalPhotos.Count) % OriginalPhotos.Count;
                LoadPhotosToGui();
                break;
            case Avalonia.Input.Key.R:
                RotateCurrentPhoto();
                break;
            case Avalonia.Input.Key.X:
                DeleteCurrentPhoto();
                break;
        }
    }

    protected override void OnClosed(System.EventArgs e)
    {
        base.OnClosed(e);
        foreach (var photo in OriginalPhotos)
        {
            photo.Dispose();
        }
        OriginalPhotos.Clear();
    }
    private void LoadCroppedPhotosToSlider()
    {
        slides.Items.Clear();

        foreach (var photo in OriginalPhotos[currentIndex].DetectedPhotos)
        {
            using var systemBitmap = photo.ToBitmap();
            slides.Items.Add(ConvertToAvaloniaBitmap(systemBitmap));
        }
    }

    private static Bitmap ConvertToAvaloniaBitmap(System.Drawing.Bitmap systemBitmap)
    {
        using MemoryStream memoryStream = new();
        systemBitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
        memoryStream.Seek(0, SeekOrigin.Begin);
        return new Bitmap(memoryStream);
    }
}