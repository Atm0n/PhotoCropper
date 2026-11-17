using PhotoCropper.Cli.Models;
using PhotoCropper.Core.Export;
using PhotoCropper.Core.Models;
using PhotoCropper.Core.Workspace;
using Spectre.Console;
using System.Diagnostics;
using System.Globalization;

namespace PhotoCropper.Cli.Services;

/// <summary>
/// The CLI's restore stage (--stage restore): applies colour restoration, dust removal and metadata to existing
/// photo files without any detection or cropping, e.g. photos already cropped by the crop stage or elsewhere.
/// </summary>
internal static class RestoreProcessor
{
    /// <summary>Default output folder name, next to each input photo, when -o isn't given.</summary>
    internal const string DefaultOutputFolderName = "restored";

    public static int Execute(CliOptions options, IReadOnlyList<string> photoFiles)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(photoFiles);

        var restoration = new RestorationSettings { RestoreColors = options.RestoreColors, RemoveDust = options.RemoveDust };
        var metadata = CreateExportMetadata(options);

        // Restored photos keep their names unless a pattern was asked for: there is one photo per file.
        string pattern = options.FileNamePatternSpecified ? options.FileNamePattern : "{original}";

        PrintHeader(options, photoFiles.Count, restoration, pattern);

        int restored = 0;
        int errors = 0;
        var stopwatch = Stopwatch.StartNew();

        AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
            .Start(ctx =>
            {
                var task = ctx.AddTask("[green]Restoring Photos[/]", maxValue: photoFiles.Count);

                Parallel.ForEach(photoFiles, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Threads) }, path =>
                {
                    try
                    {
                        string outputDir = options.OutputDirectory
                            ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", DefaultOutputFolderName);
                        var written = RestoreStage.RestoreFile(path, outputDir, restoration, options.Format, options.JpegQuality, pattern, metadata);
                        Interlocked.Increment(ref restored);
                        if (options.Verbose)
                        {
                            AnsiConsole.MarkupLine($"  [green]✓[/] {Markup.Escape(Path.GetFileName(path))} → {Markup.Escape(written.Path)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref errors);
                        AnsiConsole.MarkupLine($"  [red]✗[/] {Markup.Escape(Path.GetFileName(path))}: {Markup.Escape(ex.Message)}");
                    }
                    finally
                    {
                        task.Increment(1);
                    }
                });
            });

        stopwatch.Stop();

        var summary = new Table().Border(TableBorder.Rounded).BorderColor(Color.Cyan1)
            .AddColumn("[bold]Metric[/]").AddColumn("[bold]Result[/]");
        summary.AddRow("Photos Found", $"[bold white]{photoFiles.Count}[/]");
        summary.AddRow("Photos Restored", $"[bold green]{restored}[/]");
        summary.AddRow("Errors", errors > 0 ? $"[bold red]{errors}[/]" : "[green]0[/]");
        summary.AddRow("Total Time", $"{stopwatch.Elapsed.TotalSeconds.ToString("0.00", CultureInfo.CurrentCulture)}s");
        AnsiConsole.Write(new Panel(summary).Header("[bold cyan]Restore Summary[/]").Border(BoxBorder.Rounded));

        return errors == 0 ? 0 : 1;
    }

    private static void PrintHeader(CliOptions options, int count, RestorationSettings restoration, string pattern)
    {
        var grid = new Grid();
        grid.AddColumn(new GridColumn().PadRight(2));
        grid.AddColumn(new GridColumn());
        grid.AddRow("[bold cyan]Photos Found:[/]", $"[bold white]{count}[/]");
        grid.AddRow("[bold cyan]Stage:[/]", "[bold yellow]Restore only[/] (no detection or cropping)");
        grid.AddRow("[bold cyan]Output Format:[/]", $"[bold white]{options.Format}[/] (Quality: {options.JpegQuality})");
        grid.AddRow("[bold cyan]Restoration:[/]", restoration.RestoreColors ? "[bold green]Enabled[/]" : "[grey]Disabled[/]");
        grid.AddRow("[bold cyan]Dust Inpainting:[/]", restoration.RestoreColors && restoration.RemoveDust ? "[bold green]Enabled[/]" : "[grey]Disabled[/]");
        grid.AddRow("[bold cyan]Naming Pattern:[/]", $"[bold white]{Markup.Escape(pattern)}[/]");
        grid.AddRow("[bold cyan]Output:[/]", Markup.Escape(options.OutputDirectory ?? $"<photo folder>/{DefaultOutputFolderName}"));
        AnsiConsole.Write(new Panel(grid).Header("[bold cyan]PhotoCropper CLI - Restore[/]").Border(BoxBorder.Rounded).BorderColor(Color.Cyan1));
    }

    private static PhotoExportMetadata? CreateExportMetadata(CliOptions options)
    {
        DateTime? date = !string.IsNullOrWhiteSpace(options.Date) &&
                         DateTime.TryParse(options.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;

        var metadata = new PhotoExportMetadata
        {
            Year = options.Year,
            DateTaken = date,
            Description = options.Description,
            Photographer = options.Photographer
        };
        return metadata.HasMetadata ? metadata : null;
    }
}
