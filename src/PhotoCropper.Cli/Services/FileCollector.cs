using PhotoCropper.Core.IO;

namespace PhotoCropper.Cli.Services;

internal static class FileCollector
{
    public static List<string> CollectFiles(IEnumerable<string> inputs, bool recursive)
    {
        return ImageFileCollector.CollectFiles(inputs, recursive).ToList();
    }
}
