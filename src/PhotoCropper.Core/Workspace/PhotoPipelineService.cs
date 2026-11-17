using Emgu.CV;
using Emgu.CV.Structure;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Export;
using PhotoCropper.Core.Models;
using System.Drawing;

namespace PhotoCropper.Core.Workspace;

/// <summary>A geometry-only photo cropped from a scan, with the box it came from.</summary>
public sealed record CroppedPhotoInput(Mat Image, WorkspaceCropData Crop);

/// <summary>How final photos are written.</summary>
public sealed record FinalRenderOptions
{
    public string Format { get; init; } = AppConstants.DefaultImageFormat;
    public int JpegQuality { get; init; } = 100;
    public string FileNamePattern { get; init; } = FileNameTemplateHelper.DefaultPattern;

    /// <summary>Metadata for photos that have none of their own.</summary>
    public PhotoExportMetadata? DefaultMetadata { get; init; }

    /// <summary>Where final photos go; null for the project's Final/ folder.</summary>
    public string? OutputDirectory { get; init; }
}

/// <summary>
/// The project folder pipeline: RawScans → Cropped/{id}.png (geometry only, lossless) → Final/ (restored,
/// with metadata). Each stage reads the previous folder and writes its own, so earlier files are never
/// changed and any stage can be redone or started from imported files.
/// </summary>
/// <remarks>
/// Methods may be called for different scans in parallel on the same session: changes to the session's photo
/// list and counters are made under a lock on the session object.
/// </remarks>
public static class PhotoPipelineService
{
    /// <summary>Boxes overlapping at least this much (intersection over union) are treated as the same photo.</summary>
    internal const double SamePhotoOverlap = 0.5;

    /// <summary>
    /// Writes a scan's cropped photos to Cropped/ and updates their records. Photos whose box matches an existing
    /// photo of the scan keep its ID, number, restoration settings and metadata; photos no longer present are
    /// removed along with their files (if unchanged on disk). A cropped file whose content didn't change is
    /// left untouched, so its final photo isn't needlessly marked out of date.
    /// </summary>
    /// <param name="restorationForNewPhotos">Restoration settings given to photos that didn't exist before.</param>
    /// <returns>The scan's photo records, in the order of <paramref name="photos"/>.</returns>
    public static IReadOnlyList<ProjectPhoto> SaveCroppedPhotos(
        string workDirectory,
        WorkspaceSessionState session,
        WorkspaceScanEntry scan,
        IReadOnlyList<CroppedPhotoInput> photos,
        RestorationSettings restorationForNewPhotos)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentNullException.ThrowIfNull(restorationForNewPhotos);

        string scanFullPath = Path.GetFullPath(Path.Combine(workDirectory, scan.RelativePath));
        string scanBaseName = Path.GetFileNameWithoutExtension(scan.RelativePath);
        var (xDpi, yDpi) = PhotoExporter.GetDpiFromSource(scanFullPath);

        // 1. Match the new boxes to the scan's existing photos and assign IDs (under the session lock).
        var records = new ProjectPhoto[photos.Count];
        List<ProjectPhoto> removed;
        lock (session)
        {
            var existing = session.Photos
                .Where(p => string.Equals(p.ScanRelativePath, scan.RelativePath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var unmatched = new List<ProjectPhoto>(existing);

            for (int i = 0; i < photos.Count; i++)
            {
                var match = unmatched
                    .Where(p => p.Crop != null)
                    .Select(p => (Photo: p, Overlap: BoxOverlap(p.Crop!, photos[i].Crop)))
                    .Where(m => m.Overlap >= SamePhotoOverlap)
                    .OrderByDescending(m => m.Overlap)
                    .Select(m => m.Photo)
                    .FirstOrDefault();

                if (match != null)
                {
                    unmatched.Remove(match);
                    records[i] = match;
                }
                else
                {
                    int number = scan.NextPhotoNumber++;
                    var created = new ProjectPhoto
                    {
                        Id = $"{scanBaseName}_p{number:D2}",
                        ScanRelativePath = scan.RelativePath,
                        Number = number,
                        Restoration = restorationForNewPhotos
                    };
                    session.Photos.Add(created);
                    records[i] = created;
                }

                records[i].Crop = photos[i].Crop;
            }

            removed = unmatched;
            foreach (var gone in removed) session.Photos.Remove(gone);
        }

        // 2. Write the cropped files (each photo has its own file, so no lock is needed).
        string croppedDir = ProjectWorkspaceService.GetCroppedDirectory(workDirectory);
        Directory.CreateDirectory(croppedDir);
        for (int i = 0; i < photos.Count; i++)
        {
            byte[] png = PhotoExporter.EncodePhoto(photos[i].Image, AppConstants.FormatPng, 100, null, xDpi, yDpi);
            records[i].Cropped = WriteIfChanged(workDirectory, Path.Combine(croppedDir, $"{records[i].Id}.png"), png, records[i].Cropped);
        }

        // 3. Remove the files of photos that no longer exist.
        foreach (var gone in removed)
        {
            DeleteIfUnchanged(workDirectory, gone.Cropped);
            DeleteIfUnchanged(workDirectory, gone.Final);
        }

        return records;
    }

    /// <summary>
    /// Imports an existing photo (e.g. one already cropped elsewhere) into Cropped/ as a lossless PNG, so it can be
    /// restored without being scanned or cropped.
    /// </summary>
    public static ProjectPhoto ImportPhoto(
        string workDirectory,
        WorkspaceSessionState session,
        string sourcePath,
        RestorationSettings restoration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(restoration);
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Photo to import not found.", sourcePath);

        using Mat image = RestoreStage.ReadImage(sourcePath);
        var (xDpi, yDpi) = PhotoExporter.GetDpiFromSource(sourcePath);
        byte[] png = PhotoExporter.EncodePhoto(image, AppConstants.FormatPng, 100, null, xDpi, yDpi);

        ProjectPhoto photo;
        lock (session)
        {
            int number = session.NextImportNumber++;
            photo = new ProjectPhoto
            {
                Id = $"import_{number:D4}",
                ImportedFrom = Path.GetFullPath(sourcePath),
                Number = number,
                Restoration = restoration
            };
            session.Photos.Add(photo);
        }

        string croppedDir = ProjectWorkspaceService.GetCroppedDirectory(workDirectory);
        Directory.CreateDirectory(croppedDir);
        photo.Cropped = WriteIfChanged(workDirectory, Path.Combine(croppedDir, $"{photo.Id}.png"), png, null);
        return photo;
    }

    /// <summary>
    /// Renders a photo's final version: reads its cropped PNG, applies its restoration settings and writes it to
    /// Final/ in the export format with metadata, replacing its previous final file if that is unchanged on disk.
    /// </summary>
    public static void RenderFinal(string workDirectory, WorkspaceSessionState session, ProjectPhoto photo, FinalRenderOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(options);
        if (photo.Cropped == null) throw new InvalidOperationException($"Photo '{photo.Id}' has no cropped file to render from.");

        string croppedPath = photo.Cropped.GetFullPath(workDirectory);
        if (!File.Exists(croppedPath))
        {
            throw new FileNotFoundException($"Cropped photo for '{photo.Id}' is missing.", croppedPath);
        }

        // Record the cropped file as it is now (it may have been edited outside the app since it was written).
        var renderedFrom = ProjectFile.FromDisk(workDirectory, croppedPath);
        var restoration = photo.Restoration;

        var metadata = photo.Metadata ?? options.DefaultMetadata;
        byte[] bytes;
        using (Mat cropped = RestoreStage.ReadImage(croppedPath))
        {
            var (xDpi, yDpi) = PhotoExporter.GetDpiFromSource(croppedPath);
            bytes = RestoreStage.RestoreAndEncode(cropped, restoration, options.Format, options.JpegQuality, metadata, xDpi, yDpi);
        }

        string extension = string.Equals(options.Format, AppConstants.FormatPng, StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
        string fileName = FileNameTemplateHelper.FormatFileName(
            options.FileNamePattern,
            GetOriginalName(photo),
            photo.Number,
            CountSiblings(session, photo),
            metadata,
            extension);

        string finalDir = string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? ProjectWorkspaceService.GetFinalDirectory(workDirectory)
            : options.OutputDirectory;
        Directory.CreateDirectory(finalDir);

        // Replace this photo's previous final file, but only if it is still exactly what we wrote.
        DeleteIfUnchanged(workDirectory, photo.Final);

        string target = PhotoExporter.ResolveUniqueExportPath(Path.Combine(finalDir, fileName));
        try
        {
            File.WriteAllBytes(target, bytes);
        }
        finally
        {
            PhotoExporter.ReleaseExportPath(target);
        }

        photo.Final = ProjectFile.FromDisk(workDirectory, target);
        photo.FinalRenderedFrom = renderedFrom;
        photo.FinalRenderedWith = restoration;
    }

    /// <summary>Renders the final version of every photo whose final file is missing or out of date.</summary>
    /// <returns>The number of final photos written.</returns>
    public static int RenderStaleFinals(string workDirectory, WorkspaceSessionState session, IEnumerable<ProjectPhoto> photos, FinalRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(photos);
        int rendered = 0;
        foreach (var photo in photos.Where(p => p.IsFinalStale))
        {
            RenderFinal(workDirectory, session, photo, options);
            rendered++;
        }
        return rendered;
    }

    /// <summary>The scan's photo records, ordered by photo number.</summary>
    public static IReadOnlyList<ProjectPhoto> GetScanPhotos(WorkspaceSessionState session, string scanRelativePath)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (session)
        {
            return [.. session.Photos
                .Where(p => string.Equals(p.ScanRelativePath, scanRelativePath, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Number)];
        }
    }

    /// <summary>Overlap of two boxes as intersection over union of their axis-aligned bounds (0..1).</summary>
    internal static double BoxOverlap(WorkspaceCropData a, WorkspaceCropData b)
    {
        RectangleF ra = Bounds(a);
        RectangleF rb = Bounds(b);
        RectangleF intersection = RectangleF.Intersect(ra, rb);
        if (intersection.IsEmpty) return 0;

        double inter = (double)intersection.Width * intersection.Height;
        double union = (double)ra.Width * ra.Height + (double)rb.Width * rb.Height - inter;
        return union <= 0 ? 0 : inter / union;
    }

    private static RectangleF Bounds(WorkspaceCropData crop)
    {
        var vertices = new RotatedRect(new PointF(crop.CenterX, crop.CenterY), new SizeF(crop.Width, crop.Height), crop.Angle).GetVertices();
        float minX = vertices.Min(v => v.X), minY = vertices.Min(v => v.Y);
        return new RectangleF(minX, minY, vertices.Max(v => v.X) - minX, vertices.Max(v => v.Y) - minY);
    }

    private static string GetOriginalName(ProjectPhoto photo) =>
        Path.GetFileNameWithoutExtension(photo.ScanRelativePath ?? photo.ImportedFrom ?? photo.Id);

    private static int CountSiblings(WorkspaceSessionState session, ProjectPhoto photo)
    {
        lock (session)
        {
            return photo.ScanRelativePath != null
                ? session.Photos.Count(p => string.Equals(p.ScanRelativePath, photo.ScanRelativePath, StringComparison.OrdinalIgnoreCase))
                : session.Photos.Count(p => p.ScanRelativePath == null);
        }
    }

    /// <summary>
    /// Writes <paramref name="bytes"/> to <paramref name="path"/> unless the file already holds exactly these bytes,
    /// and returns the file's record (the existing one when nothing changed).
    /// </summary>
    private static ProjectFile WriteIfChanged(string workDirectory, string path, byte[] bytes, ProjectFile? current)
    {
        if (current != null &&
            string.Equals(current.GetFullPath(workDirectory), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase) &&
            current.IsUnchangedOnDisk(workDirectory) &&
            current.Length == bytes.Length &&
            File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
        {
            return current;
        }

        File.WriteAllBytes(path, bytes);
        return ProjectFile.FromDisk(workDirectory, path);
    }

    private static void DeleteIfUnchanged(string workDirectory, ProjectFile? file)
    {
        if (file == null || !file.IsUnchangedOnDisk(workDirectory)) return;
        try
        {
            File.Delete(file.GetFullPath(workDirectory));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning($"[PhotoCropper] Could not remove '{file.RelativePath}': {ex.Message}");
        }
    }
}
