using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Emgu.CV;
using System.IO;

namespace PhotoCropperGui;

public partial class MainWindow : Window
{
    PhotoCropper.PhotoCropper OriginalPhoto;
    public MainWindow()
    {
        InitializeComponent();

        OriginalPhoto = new PhotoCropper.PhotoCropper("C:\\Users\\nilri\\Pictures\\test.jpg");
        OriginalPhoto.DetectPhotos();
        foreach (var photo in OriginalPhoto.DetectedPhotos)
        {
            var image = new Image
            {
                Source = ConvertToAvaloniaBitmap(photo.ToBitmap()),
                Stretch = Avalonia.Media.Stretch.UniformToFill

            };
            slides.Items.Add(image);
        }
        img.Source = ConvertToAvaloniaBitmap(OriginalPhoto.OriginalWithDetected.ToBitmap());

    }

    private async void Button_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null)
        {
            return;
        }

        var storageProvider = topLevel.StorageProvider;
        var fileResult = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a File",
            FileTypeFilter =
            [
                FilePickerFileTypes.ImageAll,
            ],
            AllowMultiple = false
        });

        if (fileResult.Count > 0)
        {
            var filePath = fileResult[0].Path.LocalPath;
            OriginalPhoto = new PhotoCropper.PhotoCropper(filePath);
            OriginalPhoto.DetectPhotos();
            foreach (var photo in OriginalPhoto.DetectedPhotos)
            {
                var image = new Image
                {
                    Source =  ConvertToAvaloniaBitmap(photo.ToBitmap()),
                    Stretch = Avalonia.Media.Stretch.UniformToFill

                };
                slides.Items.Add(image);
            }
            img.Source = ConvertToAvaloniaBitmap(OriginalPhoto.OriginalWithDetected.ToBitmap());


        }
    }

    private void Button_Click_1(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        OriginalPhoto.SaveDetectedPhotos();
    }

    private void Button_Click_2(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        slides.Previous();
    }

    private void Button_Click_3(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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
        }
    }

    private static Bitmap ConvertToAvaloniaBitmap(System.Drawing.Bitmap systemBitmap)
    {
        // Save System.Drawing.Bitmap to a MemoryStream
        using (MemoryStream memoryStream = new MemoryStream())
        {
            systemBitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
            memoryStream.Seek(0, SeekOrigin.Begin);

            // Convert MemoryStream to Avalonia Bitmap
            return new Bitmap(memoryStream);
        }
    }
}