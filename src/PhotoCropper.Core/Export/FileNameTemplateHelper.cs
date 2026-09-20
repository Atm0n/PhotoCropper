using PhotoCropper.Core.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PhotoCropper.Core.Export;

public static partial class FileNameTemplateHelper
{
    public const string DefaultPattern = "{original}_{index}";
    public const string ZeroPaddedPattern = "{original}_{index:02}";
    public const string YearOriginalPattern = "{year}_{original}_{index:02}";
    public const string YearIndexPattern = "{year}_{index:02}";
    public const string PhotoPrefixPattern = "{original}_photo_{index}";

    public static readonly IReadOnlyList<string> Presets = [
        DefaultPattern,
        ZeroPaddedPattern,
        YearOriginalPattern,
        YearIndexPattern,
        PhotoPrefixPattern
    ];

    [GeneratedRegex(@"^[\W_]+|[\W_]+$")]
    private static partial Regex SeparatorTrimRegex();

    [GeneratedRegex(@"([_\-\s]){2,}")]
    private static partial Regex DuplicateSeparatorRegex();

    public static string FormatFileName(
        string? template,
        string originalFileNameWithoutExt,
        int photoIndex,
        int totalPhotos = 1,
        PhotoExportMetadata? metadata = null,
        string extension = ".jpg")
    {
        ArgumentNullException.ThrowIfNull(originalFileNameWithoutExt);
        ArgumentNullException.ThrowIfNull(extension);

        string effectiveTemplate = string.IsNullOrWhiteSpace(template) ? DefaultPattern : template.Trim();

        // Ensure extension starts with dot
        string cleanExt = extension.StartsWith('.') ? extension : $".{extension}";

        string result = effectiveTemplate;

        // Replace original/filename
        result = result.Replace("{original}", originalFileNameWithoutExt, StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{filename}", originalFileNameWithoutExt, StringComparison.OrdinalIgnoreCase);

        // Replace total
        result = result.Replace("{total}", totalPhotos.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{total:02}", totalPhotos.ToString("D2", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{total:D2}", totalPhotos.ToString("D2", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

        // Replace index formats
        result = result.Replace("{index:03}", photoIndex.ToString("D3", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{index:000}", photoIndex.ToString("D3", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{index:D3}", photoIndex.ToString("D3", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{index:02}", photoIndex.ToString("D2", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{index:00}", photoIndex.ToString("D2", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{index:D2}", photoIndex.ToString("D2", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{index}", photoIndex.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

        // Replace metadata tokens
        string yearStr = metadata?.Year?.ToString(CultureInfo.InvariantCulture) ?? "";
        string dateStr = metadata?.FormattedDate("yyyy-MM-dd") ?? (metadata?.Year?.ToString(CultureInfo.InvariantCulture) ?? "");

        result = result.Replace("{year}", yearStr, StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{date}", dateStr, StringComparison.OrdinalIgnoreCase);

        // Sanitize invalid characters
        char[] invalidChars = Path.GetInvalidFileNameChars();
        char[] resultChars = result.ToCharArray();
        for (int i = 0; i < resultChars.Length; i++)
        {
            if (Array.IndexOf(invalidChars, resultChars[i]) >= 0)
            {
                resultChars[i] = '_';
            }
        }
        result = new string(resultChars);

        // Clean up duplicate separators and leading/trailing separators if year/date was empty
        result = DuplicateSeparatorRegex().Replace(result, "$1");
        result = result.Trim();
        if (result.StartsWith('_') || result.StartsWith('-'))
        {
            result = result.TrimStart('_', '-');
        }
        if (result.EndsWith('_') || result.EndsWith('-'))
        {
            result = result.TrimEnd('_', '-');
        }

        // If after replacements result is empty, fallback to default
        if (string.IsNullOrWhiteSpace(result))
        {
            result = $"{originalFileNameWithoutExt}_{photoIndex}";
        }

        // If template already contained extension, don't duplicate
        if (result.EndsWith(cleanExt, StringComparison.OrdinalIgnoreCase))
        {
            return result;
        }

        return $"{result}{cleanExt}";
    }

    public static string FormatPreview(
        string? template,
        string sampleOriginalName = "Scan001",
        int sampleIndex = 1,
        int sampleTotal = 4,
        PhotoExportMetadata? sampleMetadata = null,
        string extension = ".jpg")
    {
        return FormatFileName(template, sampleOriginalName, sampleIndex, sampleTotal, sampleMetadata, extension);
    }
}
