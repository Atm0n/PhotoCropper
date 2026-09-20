using System.Globalization;

namespace PhotoCropper.Core.Models;

public sealed class PhotoExportMetadata : IEquatable<PhotoExportMetadata>
{
    public int? Year { get; set; }
    public DateTime? DateTaken { get; set; }
    public string? Description { get; set; }
    public string? Photographer { get; set; }

    public DateTime? EffectiveDate
    {
        get
        {
            if (DateTaken.HasValue)
            {
                return DateTaken.Value;
            }

            if (Year.HasValue && Year.Value is >= 1800 and <= 2100)
            {
                return new DateTime(Year.Value, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
            }

            return null;
        }
    }

    public string? FormattedExifDate =>
        EffectiveDate?.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);

    public string? FormattedDate(string format = "yyyy-MM-dd")
    {
        ArgumentNullException.ThrowIfNull(format);
        return EffectiveDate?.ToString(format, CultureInfo.InvariantCulture);
    }

    public bool HasMetadata =>
        Year.HasValue ||
        DateTaken.HasValue ||
        !string.IsNullOrWhiteSpace(Description) ||
        !string.IsNullOrWhiteSpace(Photographer);

    public bool Equals(PhotoExportMetadata? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Year == other.Year &&
               Nullable.Equals(DateTaken, other.DateTaken) &&
               string.Equals(Description, other.Description, StringComparison.Ordinal) &&
               string.Equals(Photographer, other.Photographer, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj) => Equals(obj as PhotoExportMetadata);

    public override int GetHashCode() =>
        HashCode.Combine(Year, DateTaken, Description, Photographer);
}
