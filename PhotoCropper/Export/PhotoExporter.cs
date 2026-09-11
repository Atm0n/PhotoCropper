using Emgu.CV;
using Emgu.CV.CvEnum;

namespace PhotoCropper.Export;

public static class PhotoExporter
{
    public static void SavePhotos(
        IReadOnlyList<Mat> photos, 
        string originalFilePath, 
        string? customOutputFolder = null, 
        string format = "JPEG", 
        int jpegQuality = 90)
    {
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentNullException.ThrowIfNull(originalFilePath);
        ArgumentNullException.ThrowIfNull(format);

        string outputFolder;
        if (!string.IsNullOrEmpty(customOutputFolder))
        {
            outputFolder = customOutputFolder;
        }
        else
        {
            string? directory = Path.GetDirectoryName(originalFilePath);
            if (string.IsNullOrEmpty(directory)) return;
            outputFolder = Path.Combine(directory, "cropped");
        }

        Directory.CreateDirectory(outputFolder);

        string baseFileName = Path.GetFileNameWithoutExtension(originalFilePath);
        string extension = string.Equals(format, "PNG", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";

        int saveCounter = 1;
        for (int i = 0; i < photos.Count; i++)
        {
            if (photos[i].IsEmpty) continue;
            string fileName = Path.Combine(outputFolder, $"{baseFileName}_{saveCounter++}{extension}");
            
            if (string.Equals(format, "PNG", StringComparison.OrdinalIgnoreCase))
            {
                photos[i].Save(fileName);
            }
            else
            {
                KeyValuePair<ImwriteFlags, int>[] parameters = [
                    new KeyValuePair<ImwriteFlags, int>(ImwriteFlags.JpegQuality, jpegQuality)
                ];
                CvInvoke.Imwrite(fileName, photos[i], parameters);
            }
        }
    }
}
