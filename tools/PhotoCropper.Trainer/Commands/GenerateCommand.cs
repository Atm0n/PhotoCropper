using System.Text.Json;
using OpenCvSharp;
using PhotoCropper.Trainer.Album;
using Spectre.Console;

namespace PhotoCropper.Trainer.Commands;

/// <summary>Options for <c>generate</c>.</summary>
/// <param name="Output">The folder for the set: images/, masks/, boxes/ and set.json.</param>
/// <param name="PhotoFolders">Folders of real public-domain photos to use (made-up pictures are used as well).</param>
/// <param name="Seed">The set's seed: the same seed and count give the same pages.</param>
internal sealed record GenerateOptions(string Output, int Count, int Size, ulong Seed, IReadOnlyList<string> PhotoFolders);

/// <summary>Writes a set of practice album pages with their masks and photo boxes.</summary>
internal static class GenerateCommand
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static int Run(IAnsiConsole console, GenerateOptions options)
    {
        if (options.Size % 16 != 0)
        {
            console.MarkupLine("[red]--size must be a multiple of 16.[/]");
            return 2;
        }

        using var library = PhotoLibrary.Load(options.PhotoFolders);
        console.MarkupLine(library.Count == 0
            ? "[yellow]No photo folders given (or no photos in them): every picture will be made up. Real photos make a better set: see the photos command.[/]"
            : $"Using {library.Count:N0} real photos, and made-up pictures.");

        string images = Path.Combine(options.Output, "images"), masks = Path.Combine(options.Output, "masks"), boxes = Path.Combine(options.Output, "boxes");
        Directory.CreateDirectory(images);
        Directory.CreateDirectory(masks);
        Directory.CreateDirectory(boxes);
        File.WriteAllText(Path.Combine(options.Output, "set.json"), JsonSerializer.Serialize(new
        {
            options.Count,
            options.Size,
            options.Seed,
            Photos = library.Count,
            MaskGap = AlbumGenerator.MaskGap,
            Created = DateTime.UtcNow,
        }, Json));

        var generator = new AlbumGenerator(library, options.Size);
        int done = 0, skipped = 0;
        console.Progress().Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new RemainingTimeColumn())
            .Start(context =>
            {
                var task = context.AddTask($"Making {options.Count:N0} pages of {options.Size} px", maxValue: options.Count);
                Parallel.For(0, options.Count, i =>
                {
                    string name = $"{i:D6}";
                    string imagePath = Path.Combine(images, name + ".jpg");
                    // Pages already there are kept: an interrupted run carries on where it stopped.
                    if (File.Exists(imagePath) && File.Exists(Path.Combine(masks, name + ".png")))
                    {
                        Interlocked.Increment(ref skipped);
                    }
                    else
                    {
                        using var page = generator.Generate(options.Seed, i);
                        Cv2.ImWrite(Path.Combine(masks, name + ".png"), page.Mask);
                        File.WriteAllText(Path.Combine(boxes, name + ".json"), JsonSerializer.Serialize(page.Boxes));
                        // The image last: its presence means the page is complete.
                        Cv2.ImWrite(imagePath, page.Image, [new ImageEncodingParam(ImwriteFlags.JpegQuality, 95)]);
                    }
                    task.Value = Interlocked.Increment(ref done);
                });
            });

        console.MarkupLine($"[green]Done:[/] {options.Count - skipped:N0} pages written, {skipped:N0} already there, in {Markup.Escape(Path.GetFullPath(options.Output))}.");
        return 0;
    }

    /// <summary>A sheet of the first pages, each next to its mask's outline, to see what the set looks like.</summary>
    public static int Preview(IAnsiConsole console, GenerateOptions options, string sheetPath)
    {
        using var library = PhotoLibrary.Load(options.PhotoFolders);
        var generator = new AlbumGenerator(library, options.Size);
        int columns = 4, rows = (int)Math.Ceiling(options.Count / (double)columns);
        int cell = options.Size;
        using Mat sheet = new(rows * cell, columns * cell, MatType.CV_8UC3, Scalar.All(40));
        var pages = new AlbumPage?[options.Count];
        try
        {
            Parallel.For(0, options.Count, i => pages[i] = generator.Generate(options.Seed, i));
            for (int i = 0; i < options.Count; i++)
            {
                var page = pages[i]!;
                using Mat shown = page.Image.Clone();
                Cv2.FindContours(page.Mask, out Point[][] outlines, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                Cv2.DrawContours(shown, outlines, -1, new Scalar(0, 0, 255), 2, LineTypes.AntiAlias);
                using Mat place = sheet[new Rect((i % columns) * cell, (i / columns) * cell, cell, cell)];
                shown.CopyTo(place);
            }
        }
        finally
        {
            foreach (var page in pages) page?.Dispose();
        }
        string? folder = Path.GetDirectoryName(Path.GetFullPath(sheetPath));
        if (folder != null) Directory.CreateDirectory(folder);
        Cv2.ImWrite(sheetPath, sheet);
        console.MarkupLine($"Preview of {options.Count} pages (mask outlines in red): {Markup.Escape(Path.GetFullPath(sheetPath))}");
        return 0;
    }
}
