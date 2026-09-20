using PhotoCropper.Core.Workspace;

namespace PhotoCropper.Core.Export;

public static class ExportPathResolver
{
    public static string ResolveOutputDirectory(string scanFilePath, string? customOutputFolder = null, string? workDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(scanFilePath);

        if (!string.IsNullOrWhiteSpace(customOutputFolder))
        {
            return customOutputFolder;
        }

        if (!string.IsNullOrWhiteSpace(workDirectory) && Directory.Exists(workDirectory))
        {
            return ProjectWorkspaceService.GetCroppedDirectory(workDirectory);
        }

        string? directory = Path.GetDirectoryName(scanFilePath);
        if (string.IsNullOrEmpty(directory))
        {
            return "cropped";
        }

        string folderName = Path.GetFileName(directory);
        if (string.Equals(folderName, ProjectWorkspaceService.RawScansFolderName, StringComparison.OrdinalIgnoreCase))
        {
            string? parentDir = Path.GetDirectoryName(directory);
            return !string.IsNullOrEmpty(parentDir)
                ? Path.Combine(parentDir, ProjectWorkspaceService.CroppedFolderName)
                : Path.Combine(directory, ProjectWorkspaceService.CroppedFolderName);
        }

        return Path.Combine(directory, "cropped");
    }

    public static bool IsScanExported(string scanFilePath, string? customOutputFolder = null, string? workDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(scanFilePath);

        string outputDir = ResolveOutputDirectory(scanFilePath, customOutputFolder, workDirectory);
        if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir))
        {
            return false;
        }

        string baseName = Path.GetFileNameWithoutExtension(scanFilePath);
        return Directory.EnumerateFiles(outputDir, $"{baseName}_*.*").Any();
    }
}
