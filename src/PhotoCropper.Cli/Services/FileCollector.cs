namespace PhotoCropper.Cli.Services;

internal static class FileCollector
{
    private static readonly HashSet<string> ValidExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp"
    };

    public static List<string> CollectFiles(IEnumerable<string> inputs, bool recursive)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var result = new List<string>();
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        foreach (string input in inputs)
        {
            if (File.Exists(input))
            {
                if (ValidExtensions.Contains(Path.GetExtension(input)))
                {
                    result.Add(Path.GetFullPath(input));
                }
            }
            else if (Directory.Exists(input))
            {
                foreach (string ext in ValidExtensions)
                {
                    try
                    {
                        result.AddRange(Directory.GetFiles(input, $"*{ext}", searchOption).Select(Path.GetFullPath));
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Skip inaccessible directories
                    }
                }
            }
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToList();
    }
}
