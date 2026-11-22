using System.Diagnostics;

namespace PhotoCropper.Trainer.Album;

/// <summary>Times page generation on this computer, for <c>check</c>'s estimate.</summary>
internal static class AlbumTiming
{
    /// <returns>Seconds per page, with every core making pages as <c>generate</c> does.</returns>
    public static double SecondsPerPage(int size)
    {
        // Made-up pictures only: no photo files needed, and loading them costs about the same.
        using var library = PhotoLibrary.Load([]);
        var generator = new AlbumGenerator(library, size);
        int pages = Math.Max(8, Environment.ProcessorCount * 2);
        generator.Generate(seed: 1, index: 0).Dispose(); // warm up

        var watch = Stopwatch.StartNew();
        Parallel.For(0, pages, i => generator.Generate(seed: 1, index: i + 1).Dispose());
        return watch.Elapsed.TotalSeconds / pages;
    }
}
