using OpenCvSharp;
using PhotoCropper.Core.Common;
using System.Collections.Concurrent;

namespace PhotoCropper.Core.Export;

public static class PhotoExporter
{
    public static (int XDpi, int YDpi) GetDpiFromSource(string sourceFilePath)
    {
        ArgumentNullException.ThrowIfNull(sourceFilePath);
        if (!File.Exists(sourceFilePath)) return (300, 300);

        try
        {
            byte[] fileBytes = File.ReadAllBytes(sourceFilePath);
            return ExtractDpiFromBytes(fileBytes);
        }
        catch
        {
            return (300, 300);
        }
    }

    public static (int XDpi, int YDpi) ExtractDpiFromBytes(ReadOnlySpan<byte> data)
    {
        // Check JPEG JFIF / Exif headers
        if (data.Length > 20 && data[0] == 0xFF && data[1] == 0xD8)
        {
            int offset = 2;
            while (offset + 4 < data.Length)
            {
                if (data[offset] != 0xFF) break;
                byte marker = data[offset + 1];
                if (marker == 0xDA || marker == 0xD9) break; // SOS or EOI

                int length = (data[offset + 2] << 8) | data[offset + 3];
                if (offset + 2 + length > data.Length) break;

                // APP0 (JFIF)
                if (marker == 0xE0 && length >= 16)
                {
                    if (data[offset + 4] == 'J' && data[offset + 5] == 'F' && data[offset + 6] == 'I' && data[offset + 7] == 'F')
                    {
                        byte units = data[offset + 11];
                        int xDens = (data[offset + 12] << 8) | data[offset + 13];
                        int yDens = (data[offset + 14] << 8) | data[offset + 15];

                        if (units == 1 && xDens > 0 && yDens > 0) // Dots per inch
                        {
                            return (xDens, yDens);
                        }
                        if (units == 2 && xDens > 0 && yDens > 0) // Dots per cm -> convert to DPI
                        {
                            return ((int)Math.Round(xDens * 2.54), (int)Math.Round(yDens * 2.54));
                        }
                    }
                }
                offset += 2 + length;
            }
        }

        // Check PNG pHYs chunk
        if (data.Length > 30 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
        {
            int offset = 8;
            while (offset + 12 <= data.Length)
            {
                int chunkLen = (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
                if (chunkLen < 0 || offset + 12 + chunkLen > data.Length) break;

                if (data[offset + 4] == 'p' && data[offset + 5] == 'H' && data[offset + 6] == 'Y' && data[offset + 7] == 's' && chunkLen >= 9)
                {
                    int ppux = (data[offset + 8] << 24) | (data[offset + 9] << 16) | (data[offset + 10] << 8) | data[offset + 11];
                    int ppuy = (data[offset + 12] << 24) | (data[offset + 13] << 16) | (data[offset + 14] << 8) | data[offset + 15];
                    byte unit = data[offset + 16];

                    if (unit == 1 && ppux > 0 && ppuy > 0) // Meters -> convert to DPI
                    {
                        int xDpi = (int)Math.Round(ppux * 0.0254);
                        int yDpi = (int)Math.Round(ppuy * 0.0254);
                        return (xDpi, yDpi);
                    }
                }

                offset += 12 + chunkLen;
            }
        }

        return (300, 300);
    }

    public static void SavePhotos(
        IReadOnlyList<Mat> photos,
        string originalFilePath,
        string? customOutputFolder,
        string format,
        int jpegQuality,
        Action<int, int>? progressCallback)
    {
        SavePhotos(photos, originalFilePath, customOutputFolder, format, jpegQuality, FileNameTemplateHelper.DefaultPattern, null, progressCallback);
    }

    /// <param name="cleanOldExports">
    /// Remove this scan's earlier exports from the output folder before writing. When
    /// <paramref name="previousExports"/> has entries, exactly those files are removed (if unchanged on disk and
    /// still in the output folder). Otherwise, if <paramref name="matchPreviousExportsByName"/> is set, files
    /// named exactly as the current pattern produces for this scan are removed instead.
    /// </param>
    /// <param name="previousExports">Files recorded by this scan's previous export (see the return value).</param>
    /// <param name="matchPreviousExportsByName">
    /// Allow the name-based fallback. Callers should turn it off when another scan exporting to the same folder
    /// has the same base name, since names can't tell their files apart.
    /// </param>
    /// <returns>The files written, to pass as <paramref name="previousExports"/> on the next export.</returns>
    /// <exception cref="PartialExportException">Writing failed after some files were written; they are listed on the exception.</exception>
    public static IReadOnlyList<ExportedFile> SavePhotos(
        IReadOnlyList<Mat> photos,
        string originalFilePath,
        string? customOutputFolder = null,
        string format = Common.AppConstants.DefaultImageFormat,
        int jpegQuality = 100,
        string fileNamePattern = FileNameTemplateHelper.DefaultPattern,
        PhotoCropper.Core.Models.PhotoExportMetadata? metadata = null,
        Action<int, int>? progressCallback = null,
        bool cleanOldExports = false,
        IEnumerable<ExportedFile>? previousExports = null,
        bool matchPreviousExportsByName = true)
    {
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentNullException.ThrowIfNull(originalFilePath);
        ArgumentNullException.ThrowIfNull(format);

        var written = new List<ExportedFile>();
        string outputFolder = ExportPathResolver.ResolveOutputDirectory(originalFilePath, customOutputFolder);
        if (string.IsNullOrEmpty(outputFolder)) return written;

        Directory.CreateDirectory(outputFolder);

        string baseFileName = Path.GetFileNameWithoutExtension(originalFilePath);

        int totalValidPhotos = 0;
        for (int p = 0; p < photos.Count; p++)
        {
            if (!photos[p].Empty()) totalValidPhotos++;
        }

        string extension = string.Equals(format, "PNG", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
        var (xDpi, yDpi) = GetDpiFromSource(originalFilePath);

        // Encode everything before touching the output folder, so an encoding failure can't happen after the
        // previous export has already been deleted.
        var encoded = new List<(int SourceIndex, string FileName, byte[] Bytes)>(totalValidPhotos);
        int saveCounter = 1;
        for (int i = 0; i < photos.Count; i++)
        {
            if (photos[i].Empty()) continue;
            string relativeFileName = FileNameTemplateHelper.FormatFileName(
                fileNamePattern,
                baseFileName,
                saveCounter++,
                totalValidPhotos,
                metadata,
                extension);
            encoded.Add((i, relativeFileName, EncodePhoto(photos[i], format, jpegQuality, metadata, xDpi, yDpi)));
        }

        if (cleanOldExports)
        {
            CleanOldExports(outputFolder, originalFilePath, fileNamePattern, metadata, totalValidPhotos, previousExports, matchPreviousExportsByName);
        }

        foreach (var (sourceIndex, relativeFileName, fileBytes) in encoded)
        {
            string fileName = ResolveUniqueExportPath(Path.Combine(outputFolder, relativeFileName));
            try
            {
                WriteBytesToFile(fileName, fileBytes);
            }
            catch (Exception ex) when (written.Count > 0 && ex is IOException or UnauthorizedAccessException)
            {
                // Report what did get written so the caller can still record (and later replace) it.
                throw new PartialExportException(written, ex);
            }
            finally
            {
                // Once the file exists on disk, File.Exists protects the name; the claim only has to cover
                // the window between choosing the name and writing it.
                ReleaseExportPath(fileName);
            }

            written.Add(ExportedFile.FromDisk(fileName));
            progressCallback?.Invoke(sourceIndex + 1, photos.Count);
        }

        return written;
    }

    /// <summary>
    /// Encodes a photo exactly as exports write it: PNG or JPEG, with EXIF/text metadata (when present) and
    /// the source scan's DPI. Shared by batch and single-photo export so both produce identical files.
    /// </summary>
    /// <param name="format">"PNG" for PNG; anything else produces JPEG.</param>
    public static byte[] EncodePhoto(Mat photo, string format, int jpegQuality, PhotoCropper.Core.Models.PhotoExportMetadata? metadata, int xDpi, int yDpi)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(format);

        bool isPng = string.Equals(format, "PNG", StringComparison.OrdinalIgnoreCase);
        byte[] fileBytes;
        if (isPng)
        {
            Cv2.ImEncode(".png", photo, out fileBytes);
        }
        else
        {
            ImageEncodingParam[] parameters = [
                new ImageEncodingParam(ImwriteFlags.JpegQuality, jpegQuality),
                new ImageEncodingParam(ImwriteFlags.JpegOptimize, 1)
            ];
            Cv2.ImEncode(".jpg", photo, out fileBytes, parameters);
        }

        if (metadata?.HasMetadata == true)
        {
            fileBytes = isPng
                ? ExifMetadataWriter.InjectPngMetadata(fileBytes, metadata)
                : ExifMetadataWriter.InjectJpegMetadata(fileBytes, metadata);
        }

        return isPng ? ApplyPngDpi(fileBytes, xDpi, yDpi) : ApplyJpegDpi(fileBytes, xDpi, yDpi);
    }

    /// <summary>
    /// Paths handed out by <see cref="ResolveUniqueExportPath"/> whose files haven't been written yet, so two
    /// concurrent exports can't pick the same new name. Entries are released once the file is written,
    /// so this only ever holds in-flight exports.
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte> ClaimedExportPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Releases a path claimed by <see cref="ResolveUniqueExportPath"/> once its file is written (or abandoned).</summary>
    public static void ReleaseExportPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        ClaimedExportPaths.TryRemove(path, out _);
    }

    private static readonly string[] ExportExtensions = [".jpg", ".png"];

    /// <summary>Upper bound on photo indices probed when looking for previous exports.</summary>
    private const int MaxProbedExportIndex = 10_000;

    private static void CleanOldExports(
        string outputFolder,
        string originalFilePath,
        string fileNamePattern,
        PhotoCropper.Core.Models.PhotoExportMetadata? metadata,
        int totalPhotos,
        IEnumerable<ExportedFile>? previousExports,
        bool matchByName)
    {
        string sourceFullPath = Path.GetFullPath(originalFilePath);
        string outputFullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputFolder));
        var previous = previousExports?.ToList() ?? [];

        IEnumerable<string> candidates;
        if (previous.Count > 0)
        {
            // A record of exactly what this scan wrote last time is authoritative: it also covers exports
            // made with an older pattern or metadata, and never touches another scan's files even when both
            // scans share a base name. Only files still in this output folder and unchanged on disk are
            // replaced; exports to a folder the user has since moved away from are kept.
            candidates = previous
                .Where(f => f.IsUnchangedOnDisk() &&
                            string.Equals(Path.GetDirectoryName(Path.GetFullPath(f.Path)), outputFullPath, StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Path);
        }
        else if (matchByName)
        {
            // No record (first export, or an export made before records were kept): fall back to names.
            candidates = FindPreviousExports(outputFolder, Path.GetFileNameWithoutExtension(originalFilePath), fileNamePattern, metadata, totalPhotos);
        }
        else
        {
            return;
        }

        candidates = candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        foreach (string file in candidates)
        {
            // A pattern like "{original}" exporting next to the scan generates the scan's own name.
            if (string.Equals(Path.GetFullPath(file), sourceFullPath, StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Trace.TraceWarning($"[PhotoCropper] Failed to remove previous export '{file}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Finds files from an earlier export of this scan that a re-export should replace.
    /// </summary>
    /// <remarks>
    /// Only files whose names are exactly what <paramref name="fileNamePattern"/> produces for this scan
    /// (index 1, 2, … in .jpg or .png, plus the " (n)" variants <see cref="ResolveUniqueExportPath"/> adds on
    /// collisions) are returned, so unrelated files that merely share a prefix are never touched. Indices
    /// beyond <paramref name="totalPhotos"/> are included while they keep existing, to remove photos the user
    /// has since deleted. Patterns without {original}/{filename} return nothing: their names don't identify the
    /// scan, so they could belong to another scan exported into the same folder.
    /// Exports made with a different pattern or metadata aren't found; those are left in place.
    /// </remarks>
    internal static IReadOnlyList<string> FindPreviousExports(
        string outputFolder,
        string baseFileName,
        string fileNamePattern,
        PhotoCropper.Core.Models.PhotoExportMetadata? metadata,
        int totalPhotos)
    {
        ArgumentNullException.ThrowIfNull(outputFolder);
        ArgumentNullException.ThrowIfNull(baseFileName);

        var found = new List<string>();
        if (!PatternIdentifiesScan(fileNamePattern) || !Directory.Exists(outputFolder)) return found;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 1; index <= MaxProbedExportIndex; index++)
        {
            bool foundForIndex = false;
            foreach (string ext in ExportExtensions)
            {
                string name = FileNameTemplateHelper.FormatFileName(fileNamePattern, baseFileName, index, totalPhotos, metadata, ext);
                string path = Path.Combine(outputFolder, name);
                if (!seen.Add(path)) continue;

                foreach (string existing in ExistingWithDuplicateSuffixes(path))
                {
                    found.Add(existing);
                    foundForIndex = true;
                }
            }

            if (index >= totalPhotos && !foundForIndex) break;
        }

        return found;
    }

    private static bool PatternIdentifiesScan(string? fileNamePattern) =>
        string.IsNullOrWhiteSpace(fileNamePattern) ||
        fileNamePattern.Contains("{original}", StringComparison.OrdinalIgnoreCase) ||
        fileNamePattern.Contains("{filename}", StringComparison.OrdinalIgnoreCase);

    /// <summary>Yields <paramref name="path"/> and its "name (1).ext", "name (2).ext", … siblings while they exist.</summary>
    private static IEnumerable<string> ExistingWithDuplicateSuffixes(string path)
    {
        if (File.Exists(path)) yield return path;

        string dir = Path.GetDirectoryName(path) ?? string.Empty;
        string nameWithoutExt = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        for (int n = 1; n <= AppConstants.MaxExportPathRetries; n++)
        {
            string candidate = Path.Combine(dir, $"{nameWithoutExt} ({n}){ext}");
            if (!File.Exists(candidate)) yield break;
            yield return candidate;
        }
    }

    public static string ResolveUniqueExportPath(string targetPath)
    {
        ArgumentNullException.ThrowIfNull(targetPath);

        string dir = Path.GetDirectoryName(targetPath) ?? string.Empty;
        string nameWithoutExt = Path.GetFileNameWithoutExtension(targetPath);
        string ext = Path.GetExtension(targetPath);

        string candidate = targetPath;
        int counter = 1;

        while (counter <= AppConstants.MaxExportPathRetries)
        {
            if (!File.Exists(candidate) && ClaimedExportPaths.TryAdd(candidate, 0))
            {
                return candidate;
            }

            candidate = Path.Combine(dir, $"{nameWithoutExt} ({counter++}){ext}");
        }

        throw new IOException($"Could not resolve a unique export path for '{targetPath}' after {AppConstants.MaxExportPathRetries} attempts. Check that the output directory is writable and has sufficient disk space.");
    }

    private static void WriteBytesToFile(string filePath, byte[] bytes)
    {
        const int maxRetries = 5;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                fs.Write(bytes, 0, bytes.Length);
                return;
            }
            catch (IOException) when (attempt < maxRetries - 1)
            {
                Thread.Sleep(AppConstants.ExportFileRetryDelayMs);
            }
        }
    }

    public static byte[] ApplyJpegDpi(byte[] bytes, int xDpi, int yDpi)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8) return bytes;

        // If JFIF marker already exists at byte index 2
        if (bytes.Length >= 18 && bytes[2] == 0xFF && bytes[3] == 0xE0 &&
            bytes[6] == 'J' && bytes[7] == 'F' && bytes[8] == 'I' && bytes[9] == 'F')
        {
            byte[] updated = (byte[])bytes.Clone();
            updated[13] = 1; // dots per inch
            updated[14] = (byte)((xDpi >> 8) & 0xFF);
            updated[15] = (byte)(xDpi & 0xFF);
            updated[16] = (byte)((yDpi >> 8) & 0xFF);
            updated[17] = (byte)(yDpi & 0xFF);
            return updated;
        }

        // Insert new JFIF APP0 segment right after SOI (FF D8)
        byte[] jfifHeader = [
            0xFF, 0xE0,             // APP0
            0x00, 0x10,             // Length: 16 bytes
            (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, // Identifier
            0x01, 0x01,             // Version 1.1
            0x01,                   // Units: 1 = dots per inch
            (byte)((xDpi >> 8) & 0xFF), (byte)(xDpi & 0xFF), // X density
            (byte)((yDpi >> 8) & 0xFF), (byte)(yDpi & 0xFF), // Y density
            0x00, 0x00              // Thumbnail dimensions
        ];

        byte[] newBytes = new byte[bytes.Length + jfifHeader.Length];
        newBytes[0] = bytes[0];
        newBytes[1] = bytes[1];
        Buffer.BlockCopy(jfifHeader, 0, newBytes, 2, jfifHeader.Length);
        Buffer.BlockCopy(bytes, 2, newBytes, 2 + jfifHeader.Length, bytes.Length - 2);

        return newBytes;
    }

    public static byte[] ApplyPngDpi(byte[] bytes, int xDpi, int yDpi)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < 33 || bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47) return bytes;

        // Convert DPI to pixels per meter
        int ppux = (int)Math.Round(xDpi / 0.0254);
        int ppuy = (int)Math.Round(yDpi / 0.0254);

        // Find IHDR chunk length to insert pHYs right after IHDR
        int ihdrLen = (bytes[8] << 24) | (bytes[9] << 16) | (bytes[10] << 8) | bytes[11];
        int insertPos = 8 + 12 + ihdrLen; // 8 (signature) + 12 (IHDR len, type, crc) + ihdrLen

        // 9 data bytes: 4 ppux, 4 ppuy, 1 unit (1 = meters)
        byte[] chunkData = [
            (byte)'p', (byte)'H', (byte)'Y', (byte)'s',
            (byte)((ppux >> 24) & 0xFF), (byte)((ppux >> 16) & 0xFF), (byte)((ppux >> 8) & 0xFF), (byte)(ppux & 0xFF),
            (byte)((ppuy >> 24) & 0xFF), (byte)((ppuy >> 16) & 0xFF), (byte)((ppuy >> 8) & 0xFF), (byte)(ppuy & 0xFF),
            0x01
        ];

        uint crc = CalculatePngCrc(chunkData);

        byte[] physChunk = [
            0x00, 0x00, 0x00, 0x09, // Data length = 9
            chunkData[0], chunkData[1], chunkData[2], chunkData[3],
            chunkData[4], chunkData[5], chunkData[6], chunkData[7],
            chunkData[8], chunkData[9], chunkData[10], chunkData[11],
            chunkData[12],
            (byte)((crc >> 24) & 0xFF), (byte)((crc >> 16) & 0xFF), (byte)((crc >> 8) & 0xFF), (byte)(crc & 0xFF)
        ];

        byte[] newBytes = new byte[bytes.Length + physChunk.Length];
        Buffer.BlockCopy(bytes, 0, newBytes, 0, insertPos);
        Buffer.BlockCopy(physChunk, 0, newBytes, insertPos, physChunk.Length);
        Buffer.BlockCopy(bytes, insertPos, newBytes, insertPos + physChunk.Length, bytes.Length - insertPos);

        return newBytes;
    }

    private static uint CalculatePngCrc(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }
        }
        return ~crc;
    }
}
