using PhotoCropper.Cli.Models;
using PhotoCropper.Core;
using PhotoCropper.Core.Common;
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

        PrintHeader(options, scanFiles.Count);

        var detectionOptions = CreateDetectionOptions(options);
        var metadata = CreateExportMetadata(options);

        PhotoExporter.ClearClaimedExportPaths();

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
            var passResult = RunBatchPass(scanFiles, parallelOptions, options, detectionOptions, metadata, undetectedScans);
            totalExtracted = passResult.Extracted;
            errorCount = passResult.Errors;
        }

        totalStopwatch.Stop();

        if (!undetectedScans.IsEmpty)
        {
            WriteUndetectedAuditLog(undetectedScans, options, scanFiles.Count, scanFiles);
        }

        PrintSummary(scanFiles.Count, totalExtracted, undetectedScans.Count, errorCount, totalStopwatch.Elapsed);

        HandleUndetectedReview(options, detectionOptions, metadata, undetectedScans, parallelOptions, scanFiles, totalExtracted);

        return errorCount == 0 ? 0 : 1;
    }

    private static (int Extracted, int Errors) RunBatchPass(
        IReadOnlyList<string> scanFiles,
        ParallelOptions parallelOptions,
        CliOptions options,
        DetectionOptions detectionOptions,
        PhotoExportMetadata? metadata,
        ConcurrentBag<string> undetectedScans)
    {
        int extracted = 0;
        int errors = 0;
        int completedScans = 0;

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
                        bool isUnderDetected = photoCount < options.MinExpectedPhotos;
                        bool isOverDetected = photoCount > options.MaxExpectedPhotos;
                        bool isLowCoverage = options.AutoAdjustLowCoverage && !options.AutoTune && engine.TotalDetectedAreaRatio < (options.MinCoverageThresholdPercent / 100.0);
                        bool shouldAutoTune = options.AlwaysAutoTune || (((isUnderDetected || isOverDetected) && options.AutoTune) || isLowCoverage);

                        if (shouldAutoTune)
                        {
                            var tuneResult = engine.AutoTune(options.MinExpectedPhotos, options.MaxExpectedPhotos);
                            if (tuneResult.PhotoCount != photoCount || tuneResult.Improved)
                            {
                                photoCount = tuneResult.PhotoCount;
                                wasAutoTuned = true;
                                isUnderDetected = photoCount < options.MinExpectedPhotos;
                                isOverDetected = photoCount > options.MaxExpectedPhotos;
                            }
                        }

                        if (photoCount > 0)
                        {
                            PhotoExporter.SavePhotos(
                                engine.DetectedPhotos,
                                scanPath,
                                options.OutputDirectory,
                                options.Format,
                                options.JpegQuality,
                                options.FileNamePattern,
                                metadata);

                            Interlocked.Add(ref extracted, photoCount);

                            if (isUnderDetected)
                            {
                                undetectedScans.Add(scanPath);
                                AnsiConsole.MarkupLine($"[grey][[{DateTime.Now:HH:mm:ss}]][/] [yellow]⚠ under-detected[/] [bold white]{fileName}[/] -> [yellow]{photoCount}[/] photo(s) (expected >= {options.MinExpectedPhotos})");
                            }
                            else if (isOverDetected)
                            {
                                undetectedScans.Add(scanPath);
                                AnsiConsole.MarkupLine($"[grey][[{DateTime.Now:HH:mm:ss}]][/] [yellow]⚠ over-detected[/] [bold white]{fileName}[/] -> [yellow]{photoCount}[/] photo(s) (expected <= {options.MaxExpectedPhotos})");
                            }
                            else
                            {
                                string tag = wasAutoTuned ? "[yellow]⚡ auto-tuned[/]" : "[green]✓ extracted[/]";
                                AnsiConsole.MarkupLine($"[grey][[{DateTime.Now:HH:mm:ss}]][/] {tag} [bold white]{fileName}[/] -> [green]{photoCount}[/] photo(s)");
                            }

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
                        Interlocked.Increment(ref errors);
                        AnsiConsole.MarkupLine($"[grey][[{DateTime.Now:HH:mm:ss}]][/] [bold red]✗ FAILED[/] [bold white]{fileName}[/]: [red]{Markup.Escape(ex.Message)}[/]");
                    }
                    finally
                    {
                        progressTask.Increment(1);
                        int done = Interlocked.Increment(ref completedScans);
                        if (done % 25 == 0)
                        {
                            GC.Collect(1, GCCollectionMode.Optimized, false);
                        }
                    }
                });
            });

        return (extracted, errors);
    }

    private static (int Extracted, int Recovered, ConcurrentBag<string> Remaining) RunAutoTunePass(
        List<string> unlist,
        ParallelOptions parallelOptions,
        CliOptions options,
        DetectionOptions detectionOptions,
        PhotoExportMetadata? metadata)
    {
        var remainingUndetected = new ConcurrentBag<string>();
        int extracted = 0;
        int recoveredCount = 0;

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
                        var tuneResult = engine.AutoTune(options.MinExpectedPhotos, options.MaxExpectedPhotos);

                        int photoCount = engine.DetectedPhotos.Count;
                        bool withinRange = photoCount >= options.MinExpectedPhotos && photoCount <= options.MaxExpectedPhotos;

                        if (photoCount > 0 && withinRange)
                        {
                            PhotoExporter.SavePhotos(
                                engine.DetectedPhotos,
                                scanPath,
                                options.OutputDirectory,
                                options.Format,
                                options.JpegQuality,
                                options.FileNamePattern,
                                metadata);

                            Interlocked.Add(ref extracted, photoCount);
                            Interlocked.Increment(ref recoveredCount);

                            AnsiConsole.MarkupLine($"[grey][[{DateTime.Now:HH:mm:ss}]][/] [bold green]⚡ Recovered[/] [bold white]{fileName}[/] -> [green]{photoCount}[/] photo(s) (Tolerance: {tuneResult.BestOptions.BackgroundTolerance:0})");
                        }
                        else if (photoCount > 0)
                        {
                            PhotoExporter.SavePhotos(
                                engine.DetectedPhotos,
                                scanPath,
                                options.OutputDirectory,
                                options.Format,
                                options.JpegQuality,
                                options.FileNamePattern,
                                metadata);

                            Interlocked.Add(ref extracted, photoCount);
                            remainingUndetected.Add(scanPath);
                            AnsiConsole.MarkupLine($"[grey][[{DateTime.Now:HH:mm:ss}]][/] [yellow]⚠ Still out of bounds[/] [bold white]{fileName}[/] -> {photoCount} photo(s)");
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

        return (extracted, recoveredCount, remainingUndetected);
    }

    private static void HandleUndetectedReview(
        CliOptions options,
        DetectionOptions detectionOptions,
        PhotoExportMetadata? metadata,
        ConcurrentBag<string> undetectedScans,
        ParallelOptions parallelOptions,
        IReadOnlyList<string> scanFiles,
        int totalExtracted)
    {
        if (!options.AlwaysAutoTune && !options.AutoTune && !options.NonInteractive && !undetectedScans.IsEmpty && !Console.IsInputRedirected)
        {
            string underDetectedText = options.MaxExpectedPhotos < int.MaxValue
                ? $"{undetectedScans.Count} scan(s) had photo count outside expected range ({options.MinExpectedPhotos}-{options.MaxExpectedPhotos})"
                : options.MinExpectedPhotos > 1
                    ? $"{undetectedScans.Count} scan(s) had fewer than {options.MinExpectedPhotos} photo(s) detected"
                    : $"{undetectedScans.Count} scan(s) had 0 photos detected";

            bool runAutoTune = AnsiConsole.Confirm(
                $"[yellow]⚠ {underDetectedText}. Would you like to run Auto-Tune on them now?[/]",
                defaultValue: false);

            if (runAutoTune)
            {
                var unlist = undetectedScans.ToList();
                var tuneResult = RunAutoTunePass(unlist, parallelOptions, options, detectionOptions, metadata);

                WriteUndetectedAuditLog(tuneResult.Remaining, options, scanFiles.Count, scanFiles);

                var reviewTable = new Table()
                    .Border(TableBorder.Rounded)
                    .AddColumn("[bold]Auto-Tune Metric[/]")
                    .AddColumn("[bold]Count[/]");

                reviewTable.AddRow("Scans Recovered", $"[bold green]{tuneResult.Recovered}[/]");
                reviewTable.AddRow("Remaining Undetected", $"[bold yellow]{tuneResult.Remaining.Count}[/]");
                reviewTable.AddRow("Total Photos Saved", $"[bold cyan]{totalExtracted + tuneResult.Extracted}[/]");

                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Panel(reviewTable).Header("[bold green]Auto-Tune Results[/]").Border(BoxBorder.Rounded));
            }
        }
    }

    private static void PrintSummary(int scanFilesCount, int totalExtracted, int undetectedCount, int errorCount, TimeSpan elapsed)
    {
        var summaryTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .AddColumn("[bold]Metric[/]")
            .AddColumn("[bold]Result[/]");

        summaryTable.AddRow("Total Scans Processed", $"[bold white]{scanFilesCount}[/]");
        summaryTable.AddRow("Photos Extracted", $"[bold green]{totalExtracted}[/]");
        summaryTable.AddRow("Undetected Scans", undetectedCount == 0 ? "[green]0[/]" : $"[bold yellow]{undetectedCount}[/]");
        summaryTable.AddRow("Errors", errorCount == 0 ? "[green]0[/]" : $"[bold red]{errorCount}[/]");
        summaryTable.AddRow("Total Time", $"[bold cyan]{elapsed.TotalSeconds:F2}s[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(summaryTable).Header("[bold cyan]Batch Summary[/]").Border(BoxBorder.Rounded));
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

    private static void PrintHeader(CliOptions options, int scanCount)
    {
        var grid = new Grid();
        grid.AddColumn(new GridColumn().PadRight(2));
        grid.AddColumn(new GridColumn());

        grid.AddRow("[bold cyan]Scans Found:[/]", $"[bold white]{scanCount}[/]");
        grid.AddRow("[bold cyan]Output Format:[/]", $"[bold white]{options.Format}[/] (Quality: {options.JpegQuality})");
        grid.AddRow("[bold cyan]Detection:[/]", $"Tolerance: [bold white]{options.Tolerance}[/], MinSize: [bold white]{options.MinAreaFactor * 100:0}%[/], MaxSize: [bold white]{options.MaxAreaFactor * 100:0}%[/]");
        grid.AddRow("[bold cyan]Auto-Tune:[/]", options.AlwaysAutoTune ? "[bold green]Always[/]" : options.AutoTune ? "[bold yellow]On Mismatch[/]" : options.AutoAdjustLowCoverage ? $"[bold yellow]Low-Coverage Trigger (< {options.MinCoverageThresholdPercent:0}%)[/]" : "[grey]Disabled[/]");
        grid.AddRow("[bold cyan]Auto-Orient:[/]", options.AutoOrient ? "[bold green]Enabled (AI Face + Sky)[/]" : "[grey]Disabled[/]");
        grid.AddRow("[bold cyan]Restoration:[/]", options.RestoreColors ? "[bold green]Enabled (Auto-WB + CLAHE)[/]" : "[grey]Disabled[/]");
        grid.AddRow("[bold cyan]Dust Inpainting:[/]", options.RemoveDust ? "[bold green]Enabled (Morphological)[/]" : "[grey]Disabled[/]");
        if (options.MinExpectedPhotos > 1 || options.MaxExpectedPhotos < int.MaxValue)
        {
            string rangeText = options.MaxExpectedPhotos < int.MaxValue
                ? $"{options.MinExpectedPhotos} - {options.MaxExpectedPhotos}"
                : $"{options.MinExpectedPhotos}+";
            grid.AddRow("[bold cyan]Expected Photos:[/]", $"[bold white]{rangeText}[/]");
        }
        if (!string.Equals(options.FileNamePattern, FileNameTemplateHelper.DefaultPattern, StringComparison.Ordinal))
        {
            grid.AddRow("[bold cyan]Naming Pattern:[/]", $"[bold yellow]{options.FileNamePattern}[/]");
        }
        if (options.Year.HasValue || !string.IsNullOrWhiteSpace(options.Date) || !string.IsNullOrWhiteSpace(options.Description))
        {
            string metaSummary = "";
            if (options.Year.HasValue) metaSummary += $"Year: {options.Year.Value} ";
            if (!string.IsNullOrWhiteSpace(options.Date)) metaSummary += $"Date: {options.Date} ";
            if (!string.IsNullOrWhiteSpace(options.Description)) metaSummary += $"Desc: '{options.Description}'";
            grid.AddRow("[bold cyan]EXIF Metadata:[/]", $"[bold green]{metaSummary.Trim()}[/]");
        }
        if (options.CopyUndetectedDirectory != null)
        {
            grid.AddRow("[bold cyan]Isolation Dir:[/]", $"[yellow]{options.CopyUndetectedDirectory}[/]");
        }

        AnsiConsole.Write(
            new Panel(grid)
                .Header("[bold cyan]PhotoCropper CLI - Batch Extractor[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Cyan1));
    }

    private static DetectionOptions CreateDetectionOptions(CliOptions options)
    {
        return new DetectionOptions
        {
            BackgroundTolerance = options.Tolerance,
            MinAreaFactor = options.MinAreaFactor,
            MaxAreaFactor = options.MaxAreaFactor,
            CannyLowThreshold = options.CannyLow,
            CannyHighThreshold = options.CannyLow * AppConstants.DefaultCannyHighRatio,
            AutoOrientPhotos = options.AutoOrient,
            RestoreVintageColors = options.RestoreColors,
            RemoveDustAndScratches = options.RemoveDust
        };
    }

    private static PhotoExportMetadata? CreateExportMetadata(CliOptions options)
    {
        DateTime? parsedDate = null;
        if (!string.IsNullOrWhiteSpace(options.Date) && DateTime.TryParse(options.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            parsedDate = d;
        }

        if (options.Year.HasValue || parsedDate.HasValue || !string.IsNullOrWhiteSpace(options.Description))
        {
            return new PhotoExportMetadata
            {
                Year = options.Year,
                DateTaken = parsedDate,
                Description = options.Description
            };
        }
        return null;
    }
}
