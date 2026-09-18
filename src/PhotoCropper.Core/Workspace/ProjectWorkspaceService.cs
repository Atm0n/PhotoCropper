using Emgu.CV;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PhotoCropper.Core.Workspace;

public static partial class ProjectWorkspaceService
{
    public const string RawScansFolderName = "RawScans";
    public const string CroppedFolderName = "Cropped";
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

        session.LastUpdatedUtc = DateTime.UtcNow;
        string json = JsonSerializer.Serialize(session, JsonOptions);
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

        try
        {
            string json = File.ReadAllText(sessionPath);
            return JsonSerializer.Deserialize<WorkspaceSessionState>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
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
        string croppedDir = GetCroppedDirectory(workDirectory);
        var files = Directory.GetFiles(rawDir).OrderBy(f => f).ToList();

        var state = new WorkspaceSessionState();
        foreach (string file in files)
        {
            string baseName = Path.GetFileNameWithoutExtension(file);
            int existingCroppedCount = Directory.Exists(croppedDir)
                ? Directory.EnumerateFiles(croppedDir, $"{baseName}_*.*").Count()
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
