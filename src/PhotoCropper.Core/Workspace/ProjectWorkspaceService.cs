using Emgu.CV;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PhotoCropper.Core.Workspace;

public static partial class ProjectWorkspaceService
{
    public const string RawScansFolderName = "RawScans";
    public const string CroppedFolderName = "Cropped";
    public const string FinalFolderName = "Final";
    public const string SessionFileName = "session.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    [GeneratedRegex(@"^scan_(\d+)\.[a-zA-Z0-9]+$", RegexOptions.IgnoreCase)]
    private static partial Regex ScanNumberRegex();

    public static void InitializeWorkspace(string workDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);

        Directory.CreateDirectory(workDirectory);
        Directory.CreateDirectory(GetRawScansDirectory(workDirectory));
        Directory.CreateDirectory(GetCroppedDirectory(workDirectory));
        Directory.CreateDirectory(GetFinalDirectory(workDirectory));
    }

    public static string GetFinalDirectory(string workDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);
        return Path.Combine(workDirectory, FinalFolderName);
    }

    [GeneratedRegex(@"(_p\d{2,}|^import_\d{4,})\.png$", RegexOptions.IgnoreCase)]
    private static partial Regex PipelineCroppedNameRegex();

    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".webp"];

    private static bool IsImageFile(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Upgrades a project to the RawScans → Cropped → Final layout. Before version 2.0, exports were written to
    /// Cropped/; they are final photos, so they move to Final/ (and the scans' export records follow them).
    /// Files the pipeline itself writes to Cropped/ ("*_pNN.png", "import_NNNN.png") are never moved.
    /// </summary>
    /// <returns>True when the session was changed and should be saved.</returns>
    public static bool MigrateToFolderPipeline(string workDirectory, WorkspaceSessionState session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);
        ArgumentNullException.ThrowIfNull(session);
        if (session.Version == WorkspaceSessionState.CurrentVersion) return false;

        string croppedDir = GetCroppedDirectory(workDirectory);
        string finalDir = GetFinalDirectory(workDirectory);
        var moved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(croppedDir))
        {
            var legacyExports = Directory.GetFiles(croppedDir)
                .Where(f => IsImageFile(f) && !PipelineCroppedNameRegex().IsMatch(Path.GetFileName(f)))
                .ToList();

            if (legacyExports.Count > 0) Directory.CreateDirectory(finalDir);
            foreach (string file in legacyExports)
            {
                string target = UniquePath(Path.Combine(finalDir, Path.GetFileName(file)));
                try
                {
                    File.Move(file, target);
                    moved[Path.GetFullPath(file)] = Path.GetFullPath(target);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    System.Diagnostics.Trace.TraceWarning($"[PhotoCropper] Could not move '{file}' to Final: {ex.Message}");
                }
            }
        }

        // Keep each scan's export record pointing at its files' new location.
        foreach (var scan in session.Scans)
        {
            for (int i = 0; i < scan.ExportedFiles.Count; i++)
            {
                var record = scan.ExportedFiles[i];
                if (moved.TryGetValue(Path.GetFullPath(record.Path), out string? newPath))
                {
                    scan.ExportedFiles[i] = Export.ExportedFile.FromDisk(newPath);
                }
            }
        }

        session.Version = WorkspaceSessionState.CurrentVersion;
        return true;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        string dir = Path.GetDirectoryName(path) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        for (int n = 1; ; n++)
        {
            string candidate = Path.Combine(dir, $"{name} ({n}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    public static string GetRawScansDirectory(string workDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);
        return Path.Combine(workDirectory, RawScansFolderName);
    }

    public static string GetCroppedDirectory(string workDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);
        return Path.Combine(workDirectory, CroppedFolderName);
    }

    public static string GetSessionFilePath(string workDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);
        return Path.Combine(workDirectory, SessionFileName);
    }

    public static string GetNextScanFileName(string workDirectory, string extension = ".png")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);

        string rawDir = GetRawScansDirectory(workDirectory);
        if (!Directory.Exists(rawDir))
        {
            Directory.CreateDirectory(rawDir);
            return $"scan_0001{extension}";
        }

        int maxIndex = 0;
        var existingFiles = Directory.GetFiles(rawDir);
        foreach (string file in existingFiles)
        {
            string name = Path.GetFileName(file);
            var match = ScanNumberRegex().Match(name);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int idx))
            {
                if (idx > maxIndex) maxIndex = idx;
            }
        }

        return $"scan_{maxIndex + 1:D4}{extension}";
    }

    public static string StageRawScanFromMat(string workDirectory, Mat scanMat, string? explicitFileName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);
        ArgumentNullException.ThrowIfNull(scanMat);

        InitializeWorkspace(workDirectory);

        string fileName = explicitFileName ?? GetNextScanFileName(workDirectory, ".png");
        string destinationPath = Path.Combine(GetRawScansDirectory(workDirectory), fileName);

        CvInvoke.Imwrite(destinationPath, scanMat);
        return destinationPath;
    }

    public static string StageRawScanFromFile(string workDirectory, string sourceFilePath, string? explicitFileName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("Source scan file does not exist", sourceFilePath);
        }

        InitializeWorkspace(workDirectory);

        string ext = Path.GetExtension(sourceFilePath);
        string fileName = explicitFileName ?? GetNextScanFileName(workDirectory, string.IsNullOrEmpty(ext) ? ".png" : ext);
        string destinationPath = Path.Combine(GetRawScansDirectory(workDirectory), fileName);

        File.Copy(sourceFilePath, destinationPath, overwrite: true);
        return destinationPath;
    }

    public static void SaveSession(string workDirectory, WorkspaceSessionState session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);
        ArgumentNullException.ThrowIfNull(session);

        InitializeWorkspace(workDirectory);

        string json;
        lock (session)
        {
            // Anything this version saves uses the current layout; stamping it keeps a newly created session from
            // being mistaken for an old one (and migrated) on the next load.
            session.Version = WorkspaceSessionState.CurrentVersion;
            session.LastUpdatedUtc = DateTime.UtcNow;
            json = JsonSerializer.Serialize(session, JsonOptions);
        }
        string sessionPath = GetSessionFilePath(workDirectory);

        string tempPath = $"{sessionPath}.tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, sessionPath, overwrite: true);
    }

    public static WorkspaceSessionState? LoadSession(string workDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);

        string sessionPath = GetSessionFilePath(workDirectory);
        if (!File.Exists(sessionPath)) return null;

        WorkspaceSessionState? session;
        try
        {
            string json = File.ReadAllText(sessionPath);
            session = JsonSerializer.Deserialize<WorkspaceSessionState>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (session != null && MigrateToFolderPipeline(workDirectory, session))
        {
            SaveSession(workDirectory, session);
        }

        return session;
    }

    public static bool HasRecoverableSession(string workDirectory)
    {
        if (string.IsNullOrWhiteSpace(workDirectory) || !Directory.Exists(workDirectory))
        {
            return false;
        }

        var session = LoadSession(workDirectory);
        if (session != null && session.Scans.Count > 0)
        {
            return true;
        }

        // Also check if RawScans has uncompleted scans even if session.json is absent
        string rawDir = GetRawScansDirectory(workDirectory);
        if (Directory.Exists(rawDir) && Directory.EnumerateFiles(rawDir).Any())
        {
            return true;
        }

        return false;
    }

    public static WorkspaceSessionState ReconstructSessionFromRawFiles(string workDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);

        InitializeWorkspace(workDirectory);
        string rawDir = GetRawScansDirectory(workDirectory);
        string finalDir = GetFinalDirectory(workDirectory);
        var files = Directory.GetFiles(rawDir).OrderBy(f => f).ToList();

        // No session file: the project may still use the old layout (exports in Cropped/), so migrate it first.
        var state = new WorkspaceSessionState();
        MigrateToFolderPipeline(workDirectory, state);

        foreach (string file in files)
        {
            string baseName = Path.GetFileNameWithoutExtension(file);
            int existingCroppedCount = Directory.Exists(finalDir)
                ? Directory.EnumerateFiles(finalDir, $"{baseName}_*.*").Count()
                : 0;

            state.Scans.Add(new WorkspaceScanEntry
            {
                RelativePath = Path.GetRelativePath(workDirectory, file),
                OriginalFileName = Path.GetFileName(file),
                StagedAtUtc = File.GetCreationTimeUtc(file),
                IsProcessed = existingCroppedCount > 0,
                ExtractedPhotoCount = existingCroppedCount
            });
        }

        return state;
    }

    public static WorkspaceSessionState SyncSessionWithRawFiles(string workDirectory, WorkspaceSessionState session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);
        ArgumentNullException.ThrowIfNull(session);

        string rawDir = GetRawScansDirectory(workDirectory);
        if (!Directory.Exists(rawDir)) return session;

        var files = Directory.GetFiles(rawDir).Where(f =>
            f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
        ).OrderBy(f => f).ToList();

        bool sessionChanged = false;

        foreach (string file in files)
        {
            string relPath = Path.GetRelativePath(workDirectory, file);
            bool exists = session.Scans.Any(s => string.Equals(s.RelativePath, relPath, StringComparison.OrdinalIgnoreCase) ||
                                                 string.Equals(Path.GetFileName(s.RelativePath), Path.GetFileName(file), StringComparison.OrdinalIgnoreCase));
            if (!exists)
            {
                sessionChanged = true;
                string baseName = Path.GetFileNameWithoutExtension(file);
                string finalDir = GetFinalDirectory(workDirectory);
                int existingCroppedCount = Directory.Exists(finalDir)
                    ? Directory.EnumerateFiles(finalDir, $"{baseName}_*.*").Count()
                    : 0;

                session.Scans.Add(new WorkspaceScanEntry
                {
                    RelativePath = relPath,
                    OriginalFileName = Path.GetFileName(file),
                    StagedAtUtc = File.GetCreationTimeUtc(file),
                    IsProcessed = existingCroppedCount > 0,
                    ExtractedPhotoCount = existingCroppedCount
                });
            }
        }

        if (sessionChanged)
        {
            SaveSession(workDirectory, session);
        }

        return session;
    }

    public static bool DeleteScan(string workDirectory, string relativeOrAbsolutePath, WorkspaceSessionState? session = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeOrAbsolutePath);

        string fullPath = Path.IsPathRooted(relativeOrAbsolutePath)
            ? relativeOrAbsolutePath
            : Path.Combine(workDirectory, relativeOrAbsolutePath);

        string relativePath = Path.IsPathRooted(relativeOrAbsolutePath)
            ? Path.GetRelativePath(workDirectory, relativeOrAbsolutePath)
            : relativeOrAbsolutePath;

        bool fileDeleted = false;
        if (File.Exists(fullPath))
        {
            try
            {
                File.Delete(fullPath);
                fileDeleted = true;
            }
            catch (IOException)
            {
                // File locked or in use
            }
            catch (UnauthorizedAccessException)
            {
                // Permissions
            }
        }

        if (session != null)
        {
            var entry = session.Scans.FirstOrDefault(s =>
                string.Equals(s.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(s.RelativePath), Path.GetFileName(relativeOrAbsolutePath), StringComparison.OrdinalIgnoreCase));

            if (entry != null)
            {
                session.Scans.Remove(entry);
                SaveSession(workDirectory, session);
            }
        }

        return fileDeleted;
    }
}
