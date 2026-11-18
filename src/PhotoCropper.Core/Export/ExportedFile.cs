namespace PhotoCropper.Core.Export;

/// <summary>
/// A file written by an export, recorded so a later re-export can replace it.
/// </summary>
public sealed class ExportedFile
{
    public string Path { get; init; } = string.Empty;
    public long Length { get; init; }
    public DateTime LastWriteTimeUtc { get; init; }

    public static ExportedFile FromDisk(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var info = new FileInfo(path);
        return new ExportedFile { Path = info.FullName, Length = info.Length, LastWriteTimeUtc = info.LastWriteTimeUtc };
    }

    /// <summary>
    /// True when the file still exists exactly as this export wrote it. A missing, replaced or edited
    /// file (e.g. a different file later saved under the same name) is not ours to delete.
    /// </summary>
    public bool IsUnchangedOnDisk()
    {
        if (string.IsNullOrEmpty(Path)) return false;
        var info = new FileInfo(Path);
        return info.Exists && info.Length == Length && info.LastWriteTimeUtc == LastWriteTimeUtc;
    }
}
