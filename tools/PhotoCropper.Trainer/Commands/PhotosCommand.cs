using PhotoCropper.Trainer.Sources;
using Spectre.Console;

namespace PhotoCropper.Trainer.Commands;

/// <summary>Options for <c>photos</c>.</summary>
internal sealed record PhotosOptions(string Output, int Count, IReadOnlyList<string> Units, ulong Seed);

/// <summary>Downloads public-domain (CC0) photographs from Smithsonian Open Access to use in the album pages.</summary>
internal static class PhotosCommand
{
    public static async Task<int> RunAsync(IAnsiConsole console, PhotosOptions options, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        // A plain tool name: nothing about the user or the computer.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PhotoCropperTrainer/1.0");
        using var source = new SmithsonianPhotos(http, options.Output, message => console.MarkupLine($"[grey]{Markup.Escape(message)}[/]"));

        console.MarkupLine($"Choosing {options.Count:N0} CC0 photographs from Smithsonian Open Access ({string.Join(", ", options.Units)})...");
        var records = await source.ChooseAsync(options.Units, options.Count, new Rng(options.Seed), ct);
        if (records.Count == 0)
        {
            console.MarkupLine("[red]No photographs found.[/]");
            return 1;
        }

        int saved = 0;
        await console.Progress().Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new RemainingTimeColumn())
            .StartAsync(async context =>
            {
                var task = context.AddTask($"Downloading {records.Count:N0} photographs", maxValue: records.Count);
                saved = await source.DownloadAsync(records, done => task.Value = done, ct);
            });

        console.MarkupLine($"[green]{saved:N0} photographs[/] in {Markup.Escape(Path.GetFullPath(options.Output))} (credits in CREDITS.csv).");
        if (saved < records.Count) console.MarkupLine("[yellow]Some could not be fetched. Run the same command again to fill in the rest.[/]");
        return 0;
    }
}
