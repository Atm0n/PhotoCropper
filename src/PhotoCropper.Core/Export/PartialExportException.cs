namespace PhotoCropper.Core.Export;

/// <summary>
/// An export failed after some of its files had already been written. <see cref="WrittenFiles"/> lists them,
/// so callers can still record them and a later re-export can replace them instead of leaving orphans.
/// </summary>
public sealed class PartialExportException : IOException
{
    public IReadOnlyList<ExportedFile> WrittenFiles { get; } = [];

    public PartialExportException()
    {
    }

    public PartialExportException(string message)
        : base(message)
    {
    }

    public PartialExportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public PartialExportException(IReadOnlyList<ExportedFile> writtenFiles, Exception innerException)
        : base($"Export failed after writing {writtenFiles?.Count ?? 0} file(s): {innerException?.Message}", innerException)
    {
        WrittenFiles = writtenFiles ?? [];
    }
}
