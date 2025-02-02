using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Emgu.CV;
using System.Collections.Generic;
using System.IO;

namespace PhotoCropperGui;

public partial class MainWindow : Window
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
        if(fileResult.Count > 0)
        {
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
        img.Source = ConvertToAvaloniaBitmap(OriginalPhotos[currentIndex].OriginalWithDetected.ToBitmap());

        LoadCroppedPhotosToSlider();
    }

    private void BtnSaveImages_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        OriginalPhotos[currentIndex].SaveDetectedPhotos();
    }

    private void BtnPreviousCroppedImage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        slides.Previous();
    }

    private void BtnNextCroppedImage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        slides.Next();
    }

    private void Window_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        switch(e.Key)
        {
            case Avalonia.Input.Key.Left:
                slides.Previous();
                break;
            case Avalonia.Input.Key.Right:
                slides.Next();
                break;
            case Avalonia.Input.Key.Up:
                currentIndex++;
                if (currentIndex >= OriginalPhotos.Count)
                {
                    currentIndex = 0;
                }
                LoadPhotosToGui();
                break;
        }
    }
    private void LoadCroppedPhotosToSlider()
    {
        slides.Items.Clear();

        OriginalPhotos[currentIndex].DetectPhotos();

        foreach (var photo in OriginalPhotos[currentIndex].DetectedPhotos)
        {
            var image = new Image
            {
                Source = ConvertToAvaloniaBitmap(photo.ToBitmap()),
            };
            slides.Items.Add(image);
        }
    }

    private static Bitmap ConvertToAvaloniaBitmap(System.Drawing.Bitmap systemBitmap)
    {
        // Save System.Drawing.Bitmap to a MemoryStream
        using MemoryStream memoryStream = new();

        systemBitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Jpeg);
        memoryStream.Seek(0, SeekOrigin.Begin);

        // Convert MemoryStream to Avalonia Bitmap
        return new Bitmap(memoryStream);
    }
}