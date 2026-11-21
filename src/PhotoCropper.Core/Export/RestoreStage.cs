using OpenCvSharp;
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
    /// <summary>
    /// Applies the repair-brush <paramref name="repairs"/> (first, so damage doesn't skew the automatic colour
    /// correction), then <paramref name="restoration"/>, and encodes the result for export.
    /// </summary>
    public static byte[] RestoreAndEncode(
        Mat image,
        RestorationSettings restoration,
        string format,
        int jpegQuality,
        PhotoExportMetadata? metadata,
        int xDpi,
        int yDpi,
        IReadOnlyCollection<RepairStroke>? repairs = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(restoration);

        using Mat finalImage = Render(image, restoration, repairs);
        return PhotoExporter.EncodePhoto(finalImage, format, jpegQuality, metadata, xDpi, yDpi);
    }

    /// <summary>
    /// Repairs, restores and (when <see cref="RestorationSettings.TrimWhiteBorders"/> is set) trims the white border
    /// of <paramref name="image"/>, always returning a new Mat the caller owns.
    /// </summary>
    public static Mat Render(Mat image, RestorationSettings restoration, IReadOnlyCollection<RepairStroke>? repairs = null)
    {
        Mat rendered = RenderKeepingBorder(image, restoration, repairs, out Rect? picture);
        if (picture is not { } keep) return rendered;
        using (rendered)
        {
            return EdgeRefinementService.ApplyCrop(rendered, keep);
        }
    }

    /// <summary>
    /// Like <see cref="Render"/>, but a white border to trim is left in place and reported instead: the preview shows
    /// the whole photo (so it lines up with the original, and repair strokes stay where they were painted) with the
    /// part that will be cut marked.
    /// </summary>
    /// <param name="picture">The picture inside the border, when the border is to be trimmed; otherwise null.</param>
    public static Mat RenderKeepingBorder(Mat image, RestorationSettings restoration, IReadOnlyCollection<RepairStroke>? repairs, out Rect? picture)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(restoration);

        // Each step returns its input when it has nothing to do.
        Mat repaired = repairs is { Count: > 0 } ? PhotoRepairService.ApplyRepairs(image, repairs) : image;
        // The repaired image is only an intermediate step, freed here when it is a new one.
        using Mat? ownedRepair = ReferenceEquals(repaired, image) ? null : repaired;

        // Set by hand, or measured before colour restoration (which can tint the border).
        picture = !restoration.TrimWhiteBorders ? null
            : restoration.ManualTrim is { } manual ? manual.ToRect(repaired.Width, repaired.Height)
            : EdgeRefinementService.GetWhiteBorderRect(repaired);

        Mat restored = PhotoRestorationService.Restore(repaired, restoration);
        return ReferenceEquals(restored, repaired) ? repaired.Clone() : restored;
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
        Mat image = Cv2.ImDecode(bytes, ImreadModes.Unchanged);
        if (image.Empty())
        {
            image.Dispose();
            throw new InvalidOperationException($"Could not decode image '{path}'.");
        }
        return image;
    }
}
