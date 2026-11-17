using PhotoCropper.Core.Models;

namespace PhotoCropper.Core.Workspace;

/// <summary>
/// A file inside the project folder, recorded with its size and write time when the pipeline wrote it.
/// Paths are relative to the project folder so a project can be moved or synced.
/// </summary>
public sealed class ProjectFile
{
    public string RelativePath { get; init; } = string.Empty;
    public long Length { get; init; }
    public DateTime LastWriteTimeUtc { get; init; }

    public static ProjectFile FromDisk(string workDirectory, string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        var info = new FileInfo(fullPath);
        return new ProjectFile
        {
            RelativePath = Path.GetRelativePath(workDirectory, info.FullName),
            Length = info.Length,
            LastWriteTimeUtc = info.LastWriteTimeUtc
        };
    }

    public string GetFullPath(string workDirectory) => Path.GetFullPath(Path.Combine(workDirectory, RelativePath));

    /// <summary>
    /// True when the file still exists exactly as the pipeline wrote it. A missing, replaced or edited file
    /// is not the pipeline's to overwrite or delete.
    /// </summary>
    public bool IsUnchangedOnDisk(string workDirectory)
    {
        if (string.IsNullOrEmpty(RelativePath)) return false;
        var info = new FileInfo(GetFullPath(workDirectory));
        return info.Exists && info.Length == Length && info.LastWriteTimeUtc == LastWriteTimeUtc;
    }

    /// <summary>True when both records describe the same file content (same path, size and write time).</summary>
    public bool IsSameVersionAs(ProjectFile? other) =>
        other != null &&
        string.Equals(RelativePath, other.RelativePath, StringComparison.OrdinalIgnoreCase) &&
        Length == other.Length &&
        LastWriteTimeUtc == other.LastWriteTimeUtc;
}

/// <summary>Appearance fixes applied when a cropped photo is turned into its final version.</summary>
public sealed record RestorationSettings
{
    public bool RestoreColors { get; init; }

    /// <summary>Dust and scratch removal; only applied together with <see cref="RestoreColors"/>.</summary>
    public bool RemoveDust { get; init; }

    public static RestorationSettings None { get; } = new();
}

/// <summary>
/// One photo in the project and its history through the folder pipeline:
/// RawScans (scan) → Cropped/{Id}.png (geometry only) → Final/ (restored, with metadata).
/// </summary>
public sealed class ProjectPhoto
{
    /// <summary>Permanent ID, also the Cropped file name (e.g. "scan_0003_p02", "import_0001").</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The scan this photo was cropped from, relative to the project; null for imported photos.</summary>
    public string? ScanRelativePath { get; init; }

    /// <summary>Original location of an imported photo; null for photos cropped from a scan.</summary>
    public string? ImportedFrom { get; init; }

    /// <summary>Stable number within its scan (or import), used as {index} in final file names.</summary>
    public int Number { get; init; }

    /// <summary>The box on the scan (with rotations) that produced the cropped photo; null for imported photos.</summary>
    public WorkspaceCropData? Crop { get; set; }

    /// <summary>The geometry-only photo in Cropped/.</summary>
    public ProjectFile? Cropped { get; set; }

    public RestorationSettings Restoration { get; set; } = RestorationSettings.None;

    /// <summary>Per-photo metadata; when null, the export's default metadata is used.</summary>
    public PhotoExportMetadata? Metadata { get; set; }

    /// <summary>The final photo in Final/.</summary>
    public ProjectFile? Final { get; set; }

    /// <summary>The Cropped file version the final photo was rendered from.</summary>
    public ProjectFile? FinalRenderedFrom { get; set; }

    /// <summary>The restoration settings the final photo was rendered with.</summary>
    public RestorationSettings? FinalRenderedWith { get; set; }

    /// <summary>
    /// True when the final photo is missing or no longer matches the cropped photo or restoration settings.
    /// </summary>
    public bool IsFinalStale =>
        Final == null ||
        Cropped == null ||
        !Cropped.IsSameVersionAs(FinalRenderedFrom) ||
        FinalRenderedWith != Restoration;
}
