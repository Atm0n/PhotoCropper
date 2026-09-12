using PhotoCropper.Cli.Models;
using PhotoCropper.Core;
using PhotoCropper.Core.Export;
using PhotoCropper.Core.Models;
using Spectre.Console;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace PhotoCropper.Cli.Services;

internal static class BatchProcessor
{
    public static int Execute(CliOptions options, IReadOnlyList<string> scanFiles)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(scanFiles);

        // Header Panel
        var grid = new Grid();
        grid.AddColumn(new GridColumn().PadRight(2));
        grid.AddColumn(new GridColumn());

        grid.AddRow("[bold cyan]Scans Found:[/]", $"[bold white]{scanFiles.Count}[/]");
        grid.AddRow("[bold cyan]Output Format:[/]", $"[bold white]{options.Format}[/] (Quality: {options.JpegQuality})");
        grid.AddRow("[bold cyan]Detection:[/]", $"Tolerance: [bold white]{options.Tolerance}[/], MinSize: [bold white]{options.MinAreaFactor * 100:0}%[/], MaxSize: [bold white]{options.MaxAreaFactor * 100:0}%[/]");
        grid.AddRow("[bold cyan]Auto-Tune:[/]", options.AutoTune ? "[bold green]Enabled (Sweeping)[/]" : "[grey]Disabled[/]");
        grid.AddRow("[bold cyan]Auto-Orient:[/]", options.AutoOrient ? "[bold green]Enabled (AI Face + Sky)[/]" : "[grey]Disabled[/]");
        grid.AddRow("[bold cyan]Restoration:[/]", options.RestoreColors ? "[bold green]Enabled (Auto-WB + CLAHE)[/]" : "[grey]Disabled[/]");
        grid.AddRow("[bold cyan]Dust Inpainting:[/]", options.RemoveDust ? "[bold green]Enabled (Morphological)[/]" : "[grey]Disabled[/]");
        if (options.CopyUndetectedDirectory != null)
        {
            grid.AddRow("[bold cyan]Isolation Dir:[/]", $"[yellow]{options.CopyUndetectedDirectory}[/]");
        }

        AnsiConsole.Write(
            new Panel(grid)
                .Header("[bold cyan]PhotoCropper CLI - Batch Extractor[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Cyan1));

        var detectionOptions = new DetectionOptions
        {
            BackgroundTolerance = options.Tolerance,
            MinAreaFactor = options.MinAreaFactor,
            MaxAreaFactor = options.MaxAreaFactor,
            CannyLowThreshold = options.CannyLow,
            CannyHighThreshold = options.CannyLow * 2.5,
            AutoOrientPhotos = options.AutoOrient,
            RestoreVintageColors = options.RestoreColors,
            RemoveDustAndScratches = options.RemoveDust
        };

        int totalExtracted = 0;
        int errorCount = 0;
        var undetectedScans = new ConcurrentBag<string>();
        var totalStopwatch = Stopwatch.StartNew();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, options.Threads)
        };

        if (scanFiles.Count > 0)
        {
            AnsiConsole.Progress()
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn())
                .Start(ctx =>
                {
                    var progressTask = ctx.AddTask("[green]Processing Scans[/]", maxValue: scanFiles.Count);

                    Parallel.ForEach(scanFiles, parallelOptions, (scanPath) =>
                    {
                        string fileName = Path.GetFileName(scanPath);
                        try
                        {
                            using var engine = new PhotoCropperEngine(scanPath);
                            engine.ApplyOptions(detectionOptions);
                            engine.DetectPhotos();

                            int photoCount = engine.DetectedPhotos.Count;
                            bool wasAutoTuned = false;

                            if (photoCount == 0 && options.AutoTune)
                            {
                                var tuneResult = engine.AutoTune();
                                if (tuneResult.PhotoCount > 0)
                                {
                                    photoCount = tuneResult.PhotoCount;
                                    wasAutoTuned = true;
                                }
                            }

                            if (photoCount > 0)
                            {
                                PhotoExporter.SavePhotos(
                                    engine.DetectedPhotos,
                                    scanPath,
                                    options.OutputDirectory,
                                    options.Format,
                                    options.JpegQuality);

                                Interlocked.Add(ref totalExtracted, photoCount);

                                string tag = wasAutoTuned ? "[yellow]⚡ auto-tuned[/]" : "[green]✓ extracted[/]";
                                AnsiConsole.MarkupLine($"[grey][[{DateTime.Now:HH:mm:ss}]][/] {tag} [bold white]{fileName}[/] -> [green]{photoCount}[/] photo(s)");

                                if (options.Verbose)
                                {
                                    for (int p = 0; p < photoCount; p++)
                                    {
                                        var mat = engine.DetectedPhotos[p];
                                        AnsiConsole.MarkupLine($"  [dim]-> Photo #{p + 1}: {mat.Width}x{mat.Height} px[/]");
                                    }
                                }
                            }
                            else
                            {
                                undetectedScans.Add(scanPath);
                                AnsiConsole.MarkupLine($"[grey][[{DateTime.Now:HH:mm:ss}]][/] [yellow]⚠ 0 photos[/] [bold white]{fileName}[/]");
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref errorCount);
                            AnsiConsole.MarkupLine($"[grey][[{DateTime.Now:HH:mm:ss}]][/] [bold red]✗ FAILED[/] [bold white]{fileName}[/]: [red]{Markup.Escape(ex.Message)}[/]");
                        }
                        finally
                        {
                            progressTask.Increment(1);
                        }
                    });
                });
        }

        totalStopwatch.Stop();

        // Process undetected audit log & isolation
        if (!undetectedScans.IsEmpty)
        {
            WriteUndetectedAuditLog(undetectedScans, options, scanFiles.Count, scanFiles);
        }

        // Summary Table
        var summaryTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .AddColumn("[bold]Metric[/]")
            .AddColumn("[bold]Result[/]");

        summaryTable.AddRow("Total Scans Processed", $"[bold white]{scanFiles.Count}[/]");
        summaryTable.AddRow("Photos Extracted", $"[bold green]{totalExtracted}[/]");
        summaryTable.AddRow("Undetected Scans", undetectedScans.IsEmpty ? "[green]0[/]" : $"[bold yellow]{undetectedScans.Count}[/]");
        summaryTable.AddRow("Errors", errorCount == 0 ? "[green]0[/]" : $"[bold red]{errorCount}[/]");
        summaryTable.AddRow("Total Time", $"[bold cyan]{totalStopwatch.Elapsed.TotalSeconds:F2}s[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(summaryTable).Header("[bold cyan]Batch Summary[/]").Border(BoxBorder.Rounded));

        // Interactive Post-Batch Auto-Tune Review Prompt
        if (!options.AutoTune && !options.NonInteractive && !undetectedScans.IsEmpty && !Console.IsInputRedirected)
        {
            AnsiConsole.WriteLine();
            bool runAutoTune = AnsiConsole.Confirm(
                $"[yellow]⚠ {undetectedScans.Count} scan(s) had 0 photos detected. Would you like to run Auto-Tune on them now?[/]",
                defaultValue: false);

            if (runAutoTune)
            {
                var remainingUndetected = new ConcurrentBag<string>();
                int autoTunedRecovered = 0;
                var unlist = undetectedScans.ToList();

                AnsiConsole.Progress()
                    .AutoClear(false)
                    .HideCompleted(false)
                    .Columns(
                        new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new PercentageColumn(),
                        new RemainingTimeColumn(),
                        new SpinnerColumn())
                    .Start(ctx =>
                    {
                        var autoTuneTask = ctx.AddTask("[yellow]Auto-Tuning Undetected Scans[/]", maxValue: unlist.Count);

                        Parallel.ForEach(unlist, parallelOptions, (scanPath) =>
                        {
                            string fileName = Path.GetFileName(scanPath);
                            try
                            {
                                using var engine = new PhotoCropperEngine(scanPath);
                                engine.ApplyOptions(detectionOptions);
                                var tuneResult = engine.AutoTune();

                                int photoCount = engine.DetectedPhotos.Count;
                                if (photoCount > 0)
                                {
                                    PhotoExporter.SavePhotos(
                                        engine.DetectedPhotos,
                                        scanPath,
                                        options.OutputDirectory,
                                        options.Format,
                                        options.JpegQuality);

                                    Interlocked.Add(ref totalExtracted, photoCount);
                                    Interlocked.Increment(ref autoTunedRecovered);

                                    AnsiConsole.MarkupLine($"[grey][[{DateTime.Now:HH:mm:ss}]][/] [bold green]⚡ Recovered[/] [bold white]{fileName}[/] -> [green]{photoCount}[/] photo(s) (Tolerance: {tuneResult.BestOptions.BackgroundTolerance:0})");
                                }
                                else
                                {
                                    remainingUndetected.Add(scanPath);
                                    AnsiConsole.MarkupLine($"[grey][[{DateTime.Now:HH:mm:ss}]][/] [grey]Still 0 photos[/] [bold white]{fileName}[/]");
                                }
                            }
                            catch (Exception ex)
                            {
                                remainingUndetected.Add(scanPath);
                                AnsiConsole.MarkupLine($"[grey][[{DateTime.Now:HH:mm:ss}]][/] [bold red]✗ FAILED[/] [bold white]{fileName}[/]: [red]{Markup.Escape(ex.Message)}[/]");
                            }
                            finally
                            {
                                autoTuneTask.Increment(1);
                            }
                        });
                    });

                WriteUndetectedAuditLog(remainingUndetected, options, scanFiles.Count, scanFiles);

                var reviewTable = new Table()
                    .Border(TableBorder.Rounded)
                    .AddColumn("[bold]Auto-Tune Metric[/]")
                    .AddColumn("[bold]Count[/]");

                reviewTable.AddRow("Scans Recovered", $"[bold green]{autoTunedRecovered}[/]");
                reviewTable.AddRow("Remaining Undetected", $"[bold yellow]{remainingUndetected.Count}[/]");
                reviewTable.AddRow("Total Photos Saved", $"[bold cyan]{totalExtracted}[/]");

                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Panel(reviewTable).Header("[bold green]Auto-Tune Results[/]").Border(BoxBorder.Rounded));
            }
        }

        return errorCount == 0 ? 0 : 1;
    }

    private static void WriteUndetectedAuditLog(
        IEnumerable<string> undetectedScans,
        CliOptions options,
        int totalScansCount,
        IReadOnlyList<string> scanFiles)
    {
        var sortedUndetected = undetectedScans.OrderBy(x => x).ToList();
        string auditDir = options.OutputDirectory ?? (scanFiles.Count > 0 ? (Path.GetDirectoryName(scanFiles[0]) ?? ".") : ".");
        if (!Directory.Exists(auditDir))
        {
            Directory.CreateDirectory(auditDir);
        }
        string auditFile = Path.Combine(auditDir, "undetected_scans.txt");

        if (sortedUndetected.Count == 0)
        {
            if (File.Exists(auditFile))
            {
                File.Delete(auditFile);
            }
            return;
        }

        var logLines = new List<string>
        {
            "=== PhotoCropper Undetected Scans Audit Log ===",
            string.Format(CultureInfo.InvariantCulture, "Timestamp: {0:yyyy-MM-dd HH:mm:ss}", DateTime.Now),
            string.Format(CultureInfo.InvariantCulture, "Total Scans: {0} | Undetected Scans: {1}", totalScansCount, sortedUndetected.Count),
            "",
            "Files requiring manual review or auto-tuning:"
        };

        for (int i = 0; i < sortedUndetected.Count; i++)
        {
            logLines.Add(string.Format(CultureInfo.InvariantCulture, "{0}. {1}", i + 1, sortedUndetected[i]));
        }

        File.WriteAllLines(auditFile, logLines);

        if (options.CopyUndetectedDirectory != null)
        {
            Directory.CreateDirectory(options.CopyUndetectedDirectory);
            foreach (var undetectedFile in sortedUndetected)
            {
                if (File.Exists(undetectedFile))
                {
                    string destPath = Path.Combine(options.CopyUndetectedDirectory, Path.GetFileName(undetectedFile));
                    File.Copy(undetectedFile, destPath, overwrite: true);
                }
            }
        }
    }
}
