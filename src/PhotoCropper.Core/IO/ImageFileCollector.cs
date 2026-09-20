namespace PhotoCropper.Core.IO;

/// <summary>
/// Utility for discovering and collecting supported image files from paths and directories.
/// </summary>
public static class ImageFileCollector
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp"
    };

    /// <summary>
    /// Gets the set of supported image file extensions (with leading dot, case-insensitive).
    /// </summary>
    public static IReadOnlySet<string> SupportedExtensions => Extensions;

    /// <summary>
    /// Determines whether the specified file path has a supported image extension.
    /// </summary>
    public static bool IsSupportedImageFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        string ext = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(ext) && Extensions.Contains(ext);
    }

    /// <summary>
    /// Collects and normalizes all supported image files from the specified file and directory paths.
    /// </summary>
    /// <param name="inputs">Collection of file and/or directory paths.</param>
    /// <param name="recursive">Whether to scan directories recursively.</param>
    /// <returns>A deduplicated, sorted list of absolute image file paths.</returns>
    public static IReadOnlyList<string> CollectFiles(IEnumerable<string> inputs, bool recursive = true)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var result = new List<string>();
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        foreach (string input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input)) continue;

            if (File.Exists(input))
            {
                if (IsSupportedImageFile(input))
                {
                    result.Add(Path.GetFullPath(input));
                }
            }
            else if (Directory.Exists(input))
            {
                foreach (string ext in Extensions)
                {
                    try
                    {
                        result.AddRange(Directory.GetFiles(input, $"*{ext}", searchOption).Select(Path.GetFullPath));
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Skip folders with restricted access
                    }
                }
            }
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
