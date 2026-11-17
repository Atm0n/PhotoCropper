using PhotoCropper.Core.Models;

namespace PhotoCropper.Gui.Services;

/// <summary>
/// Decides which metadata is embedded into a scan's exported photos.
/// </summary>
internal static class ExportMetadataResolver
{
    /// <summary>
    /// When the global defaults apply to every scan, the default year and description replace the
    /// scan's own values, but fields the defaults don't cover (photographer, and an exact date that
    /// agrees with the default year) are kept instead of being dropped.
    /// </summary>
    public static PhotoExportMetadata Resolve(
        PhotoExportMetadata scanMetadata,
        bool applyDefaultsToAllScans,
        int? defaultYear,
        string? defaultDescription)
    {
        ArgumentNullException.ThrowIfNull(scanMetadata);

        if (!applyDefaultsToAllScans)
        {
            return scanMetadata;
        }

        // An exact date takes precedence over Year when writing EXIF, so only keep it when it
        // doesn't contradict the year the user chose to apply to all scans.
        bool dateAgreesWithDefaultYear = !defaultYear.HasValue || scanMetadata.DateTaken?.Year == defaultYear;

        return new PhotoExportMetadata
        {
            Year = defaultYear,
            Description = defaultDescription,
            DateTaken = dateAgreesWithDefaultYear ? scanMetadata.DateTaken : null,
            Photographer = scanMetadata.Photographer
        };
    }
}
