using OpenCvSharp;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Export;
using PhotoCropper.Core.Models;
using RectangleF = System.Drawing.RectangleF;

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
    /// Turns a photo a quarter turn, after cropping (e.g. from the Finish stage): its file in Cropped/ is rotated
    /// losslessly, its box on the scan records the turn (so the Crops stage and later saves agree), and its repair
    /// strokes and hand-set trim, kept as fractions of the photo, turn with it. The final photo becomes out of date.
    /// </summary>
    public static void RotatePhoto(string workDirectory, WorkspaceSessionState session, ProjectPhoto photo, bool clockwise)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(photo);
        if (photo.Cropped == null) return;

        string path = photo.Cropped.GetFullPath(workDirectory);
        var (xDpi, yDpi) = PhotoExporter.GetDpiFromSource(path);
        byte[] png;
        int width, height;
        using (Mat image = RestoreStage.ReadImage(path))
        using (Mat rotated = new())
        {
            width = image.Width;
            height = image.Height;
            Cv2.Rotate(image, rotated, clockwise ? RotateFlags.Rotate90Clockwise : RotateFlags.Rotate90Counterclockwise);
            png = PhotoExporter.EncodePhoto(rotated, AppConstants.FormatPng, 100, null, xDpi, yDpi);
        }

        lock (session)
        {
            int turn = clockwise ? 90 : 270;
            // The scan's saved box for this photo: the same record, or the box it was cropped from.
            var entry = photo.ScanRelativePath == null ? null : session.Scans.FirstOrDefault(s =>
                string.Equals(s.RelativePath, photo.ScanRelativePath, StringComparison.OrdinalIgnoreCase));
            var saved = photo.Crop == null || entry == null ? null
                : entry.FinalCrops.FirstOrDefault(c => ReferenceEquals(c, photo.Crop))
                  ?? entry.FinalCrops.Where(c => BoxOverlap(c, photo.Crop) >= SamePhotoOverlap).OrderByDescending(c => BoxOverlap(c, photo.Crop)).FirstOrDefault();
            if (saved != null) saved.RotationDegrees = (saved.RotationDegrees + turn) % 360;
            if (photo.Crop != null && !ReferenceEquals(photo.Crop, saved)) photo.Crop.RotationDegrees = (photo.Crop.RotationDegrees + turn) % 360;

            // Strokes and the trim are fractions of the photo: map each point to where it lands on the turned photo.
            (float X, float Y) Turn(float x, float y) => clockwise ? (1 - y, x) : (y, 1 - x);
            // A stroke's radius is a fraction of the photo's width, which is now its old height.
            var turnedRepairs = photo.Repairs.Select(stroke =>
            {
                var turnedStroke = new RepairStroke { Radius = stroke.Radius * width / height, Method = stroke.Method };
                for (int i = 0; i + 1 < stroke.Points.Count; i += 2)
                {
                    var (x, y) = Turn(stroke.Points[i], stroke.Points[i + 1]);
                    turnedStroke.Points.Add(x);
                    turnedStroke.Points.Add(y);
                }
                return turnedStroke;
            }).ToList();
            photo.Repairs.Clear();
            foreach (var stroke in turnedRepairs) photo.Repairs.Add(stroke);
            (photo.RepairImageWidth, photo.RepairImageHeight) = (photo.RepairImageHeight, photo.RepairImageWidth);

            if (photo.Restoration.ManualTrim is { } trim)
            {
                var turned = clockwise
                    ? new TrimArea { Left = 1 - trim.Bottom, Top = trim.Left, Right = 1 - trim.Top, Bottom = trim.Right }
                    : new TrimArea { Left = trim.Top, Top = 1 - trim.Right, Right = trim.Bottom, Bottom = 1 - trim.Left };
                photo.Restoration = photo.Restoration with { ManualTrim = turned };
            }

            photo.Cropped = WriteIfChanged(workDirectory, path, png, photo.Cropped);
        }
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
        string repairsFingerprint = photo.RepairsFingerprint;

        var metadata = photo.Metadata ?? options.DefaultMetadata;
        byte[] bytes;
        using (Mat cropped = RestoreStage.ReadImage(croppedPath))
        {
            var (xDpi, yDpi) = PhotoExporter.GetDpiFromSource(croppedPath);
            // Repair-brush strokes apply only if they were painted on a cropped photo of this size.
            var repairs = Extraction.PhotoRepairService.ApplicableRepairs(photo, cropped);
            bytes = RestoreStage.RestoreAndEncode(cropped, restoration, options.Format, options.JpegQuality, metadata, xDpi, yDpi, repairs);
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

        lock (FinalNamesLock)
        {
            if (IsProjectFinalDirectory(workDirectory, finalDir))
            {
                // The project's Final/ holds what the last export made: the new file replaces this photo's
                // previous one and anything else under its name that no other photo owns (left by an older
                // version, or by an export whose record was lost), along with its "name (n)" copies.
                DeleteFile(workDirectory, photo.Final);
                RemoveReplacedFinals(workDirectory, session, photo, finalDir, fileName);
            }
            else
            {
                // Another folder may hold the user's own files: replace this photo's previous file only if it is
                // still exactly what we wrote, and never overwrite anything else.
                DeleteIfUnchanged(workDirectory, photo.Final);
            }

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
        }
        photo.FinalRenderedFrom = renderedFrom;
        photo.FinalRenderedWith = restoration;
        photo.FinalRenderedRepairs = repairsFingerprint;
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

    /// <summary>
    /// Removes a discarded scan's photos from the pipeline: their records, and their Cropped/ and Final/ files
    /// if those are still exactly as the pipeline wrote them.
    /// </summary>
    /// <returns>The number of photos removed.</returns>
    public static int RemoveScanPhotos(string workDirectory, WorkspaceSessionState session, string scanRelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(scanRelativePath);

        List<ProjectPhoto> removed;
        lock (session)
        {
            removed = [.. session.Photos.Where(p => string.Equals(p.ScanRelativePath, scanRelativePath, StringComparison.OrdinalIgnoreCase))];
            foreach (var photo in removed) session.Photos.Remove(photo);
        }

        foreach (var photo in removed)
        {
            DeleteIfUnchanged(workDirectory, photo.Cropped);
            DeleteIfUnchanged(workDirectory, photo.Final);
        }
        return removed.Count;
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
        var vertices = new RotatedBox(new System.Drawing.PointF(crop.CenterX, crop.CenterY), new System.Drawing.SizeF(crop.Width, crop.Height), crop.Angle).GetVertices();
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

    /// <summary>Held while a final photo's name is chosen and its file written and recorded.</summary>
    private static readonly Lock FinalNamesLock = new();

    private static bool IsProjectFinalDirectory(string workDirectory, string directory) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(ProjectWorkspaceService.GetFinalDirectory(workDirectory))),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Deletes <paramref name="fileName"/> and its "name (n)" copies from <paramref name="finalDir"/>, except the
    /// final files of the session's other photos. Returns how many were deleted.
    /// </summary>
    internal static int RemoveReplacedFinals(string workDirectory, WorkspaceSessionState session, ProjectPhoto photo, string finalDir, string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        var sameName = new System.Text.RegularExpressions.Regex(
            "^" + System.Text.RegularExpressions.Regex.Escape(stem) + @"( \(\d+\))?" + System.Text.RegularExpressions.Regex.Escape(extension) + "$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        List<string> candidates;
        try
        {
            candidates = [.. Directory.EnumerateFiles(finalDir).Where(f => sameName.IsMatch(Path.GetFileName(f)))];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
        if (candidates.Count == 0) return 0;

        HashSet<string> others;
        lock (session)
        {
            others = new HashSet<string>(
                session.Photos.Where(p => p != photo && p.Final != null).Select(p => Path.GetFullPath(p.Final!.GetFullPath(workDirectory))),
                StringComparer.OrdinalIgnoreCase);
        }

        int deleted = 0;
        foreach (string file in candidates)
        {
            if (others.Contains(Path.GetFullPath(file))) continue;
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Trace.TraceWarning($"[PhotoCropper] Could not replace '{file}': {ex.Message}");
            }
        }
        return deleted;
    }

    private static void DeleteFile(string workDirectory, ProjectFile? file)
    {
        if (file == null) return;
        try
        {
            File.Delete(file.GetFullPath(workDirectory));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning($"[PhotoCropper] Could not remove '{file.RelativePath}': {ex.Message}");
        }
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
