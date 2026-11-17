using Emgu.CV;
using Emgu.CV.CvEnum;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Extraction;
using PhotoCropper.Core.Models;
using PhotoCropper.Core.Workspace;

namespace PhotoCropper.Core.Export;

/// <summary>
/// The restore stage on its own: turns an already-cropped photo into its final version (colour restoration,
/// dust removal, metadata, export format). Used by the project pipeline (Cropped → Final) and to restore any
/// photo files directly, e.g. photos that were cropped elsewhere.
/// </summary>
public static class RestoreStage
{
    /// <summary>Applies <paramref name="restoration"/> to <paramref name="image"/> and encodes the result for export.</summary>
    public static byte[] RestoreAndEncode(
        Mat image,
        RestorationSettings restoration,
        string format,
        int jpegQuality,
        PhotoExportMetadata? metadata,
        int xDpi,
        int yDpi)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(restoration);

        Mat? restored = null;
        try
        {
            // Restore returns the image itself when the settings change nothing.
            Mat result = PhotoRestorationService.Restore(image, restoration);
            if (!ReferenceEquals(result, image)) restored = result;

            return PhotoExporter.EncodePhoto(restored ?? image, format, jpegQuality, metadata, xDpi, yDpi);
        }
        finally
        {
            restored?.Dispose();
        }
    }

    /// <summary>
    /// Restores one photo file into <paramref name="outputDirectory"/>, keeping its DPI and naming it with
    /// <paramref name="fileNamePattern"/> ({original} is the source file name). Never overwrites an existing file.
    /// </summary>
    /// <returns>The file written.</returns>
    public static ExportedFile RestoreFile(
        string sourcePath,
        string outputDirectory,
        RestorationSettings restoration,
        string format = AppConstants.DefaultImageFormat,
        int jpegQuality = 100,
        string fileNamePattern = "{original}",
        PhotoExportMetadata? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(restoration);

        using Mat image = ReadImage(sourcePath);
        var (xDpi, yDpi) = PhotoExporter.GetDpiFromSource(sourcePath);
        byte[] bytes = RestoreAndEncode(image, restoration, format, jpegQuality, metadata, xDpi, yDpi);

        string extension = string.Equals(format, AppConstants.FormatPng, StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
        string fileName = FileNameTemplateHelper.FormatFileName(
            fileNamePattern, Path.GetFileNameWithoutExtension(sourcePath), 1, 1, metadata, extension);

        Directory.CreateDirectory(outputDirectory);
        string target = PhotoExporter.ResolveUniqueExportPath(Path.Combine(outputDirectory, fileName));
        try
        {
            File.WriteAllBytes(target, bytes);
        }
        finally
        {
            PhotoExporter.ReleaseExportPath(target);
        }

        return ExportedFile.FromDisk(target);
    }

    /// <summary>Reads an image file as stored (keeping an alpha channel if it has one).</summary>
    public static Mat ReadImage(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] bytes = File.ReadAllBytes(path);
        Mat image = new();
        CvInvoke.Imdecode(bytes, ImreadModes.Unchanged, image);
        if (image.IsEmpty)
        {
            image.Dispose();
            throw new InvalidOperationException($"Could not decode image '{path}'.");
        }
        return image;
    }
}
