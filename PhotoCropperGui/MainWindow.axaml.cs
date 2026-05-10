using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Emgu.CV;
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
                var photo = new PhotoCropper.PhotoCropper(filePath);
                OriginalPhotos.Add(photo);

            }
            LoadPhotosToGui();
        }

    }

    private void LoadPhotosToGui()
    {
        if (OriginalPhotos[currentIndex].DetectedPhotos.Count == 0)
        {
            OriginalPhotos[currentIndex].DetectPhotos();
        }

        using var bitmap = OriginalPhotos[currentIndex].OriginalWithDetected.ToBitmap();
        img.Source = ConvertToAvaloniaBitmap(bitmap);

        LoadCroppedPhotosToSlider();
    }

    private void BtnSaveImages_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        foreach (var originalPhoto in OriginalPhotos)
        {

            originalPhoto.SaveDetectedPhotos();
        }

    }

    private void BtnPreviousCroppedImage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        slides.Previous();
    }

    private void BtnNextCroppedImage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        slides.Next();
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

        if (OriginalPhotos[currentIndex].DetectedPhotos.Count == 0)
        {
            OriginalPhotos[currentIndex].DetectPhotos();
        }

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