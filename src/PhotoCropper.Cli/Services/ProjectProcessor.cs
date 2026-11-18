using PhotoCropper.Cli.Models;
using PhotoCropper.Core;
using PhotoCropper.Core.Models;
using PhotoCropper.Core.Workspace;
using Spectre.Console;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace PhotoCropper.Cli.Services;

/// <summary>
/// Project mode (--project): runs the GUI's RawScans → Cropped → Final pipeline on a project folder, so the CLI
/// and the app can share work. Input files are added to RawScans/; scans whose boxes were saved (edited in the
/// app) keep them, the rest are detected; Cropped/ and Final/ are written and session.json is updated.
/// </summary>
internal static class ProjectProcessor
{
    /// <summary>What happened to one scan.</summary>
    internal enum ScanOutcome
    {
        /// <summary>Its saved boxes were used.</summary>
        Saved,

        /// <summary>Its photos were detected now.</summary>
        Detected,

        /// <summary>Detected, but the photo count is outside the expected range (or zero).</summary>
        Flagged,

        /// <summary>It could not be processed.</summary>
        Failed
    }

    internal sealed record ScanResult(string Name, ScanOutcome Outcome, int Photos, string? Error = null);

    internal sealed record ProjectResult(IReadOnlyList<ScanResult> Scans, int Added, int FinalsWritten)
    {
        public int Photos => Scans.Sum(s => s.Photos);
        public int Failed => Scans.Count(s => s.Outcome == ScanOutcome.Failed);
    }

    public static int Execute(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ProjectDirectory)) throw new ArgumentException("No project folder given.", nameof(options));

        string workDir = Path.GetFullPath(options.ProjectDirectory);
        PrintHeader(options, workDir);

        var stopwatch = Stopwatch.StartNew();
        ProjectResult result;
        try
        {
            result = Run(options, workDir, AnsiConsole.Console);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AnsiConsole.MarkupLine($"[bold red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
        stopwatch.Stop();

        PrintSummary(result, workDir, options, stopwatch.Elapsed);
        return result.Failed == 0 ? 0 : 1;
    }

    /// <summary>Runs the pipeline on <paramref name="workDir"/> and saves its session.</summary>
    internal static ProjectResult Run(CliOptions options, string workDir, IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(console);

        ProjectWorkspaceService.InitializeWorkspace(workDir);
        var session = ProjectWorkspaceService.LoadSession(workDir) ?? ProjectWorkspaceService.ReconstructSessionFromRawFiles(workDir);
        session = ProjectWorkspaceService.SyncSessionWithRawFiles(workDir, session);

        int added = AddInputs(options, workDir, session);

        var scans = new List<ScanResult>();
        if (options.Stage != PipelineStage.Restore)
        {
            scans = CropScans(options, workDir, session, console);
        }

        int finals = 0;
        if (options.Stage != PipelineStage.Crop)
        {
            List<ProjectPhoto> photos;
            lock (session) photos = [.. session.Photos];
            finals = PhotoPipelineService.RenderStaleFinals(workDir, session, photos, new FinalRenderOptions
            {
                Format = options.Format,
                JpegQuality = options.JpegQuality,
                FileNamePattern = options.FileNamePattern,
                DefaultMetadata = BatchProcessor.CreateExportMetadata(options),
                OutputDirectory = options.OutputDirectory
            });
        }

        ProjectWorkspaceService.SaveSession(workDir, session);
        return new ProjectResult(scans, added, finals);
    }

    /// <summary>Copies the input files into RawScans/ as new scans of the project.</summary>
    private static int AddInputs(CliOptions options, string workDir, WorkspaceSessionState session)
    {
        if (options.Inputs.Count == 0) return 0;

        var files = PhotoCropper.Core.IO.ImageFileCollector.CollectFiles(options.Inputs, options.Recursive);
        string rawDir = Path.GetFullPath(ProjectWorkspaceService.GetRawScansDirectory(workDir));
        int added = 0;
        foreach (string file in files)
        {
            // The project's own scans (e.g. --project X X/RawScans) are already part of it.
            if (Path.GetFullPath(file).StartsWith(rawDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;

            string staged = ProjectWorkspaceService.StageRawScanFromFile(workDir, file);
            session.Scans.Add(new WorkspaceScanEntry
            {
                RelativePath = Path.GetRelativePath(workDir, staged),
                OriginalFileName = Path.GetFileName(file),
                StagedAtUtc = DateTime.UtcNow
            });
            added++;
        }
        return added;
    }

    private static List<ScanResult> CropScans(CliOptions options, string workDir, WorkspaceSessionState session, IAnsiConsole console)
    {
        var detection = BatchProcessor.CreateDetectionOptions(options);
        var restoration = new RestorationSettings { RestoreColors = options.RestoreColors, RemoveDust = options.RemoveDust };
        var entries = session.Scans.ToList();
        var results = new ConcurrentDictionary<int, ScanResult>();

        Parallel.For(0, entries.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Threads) }, i =>
        {
            var entry = entries[i];
            string name = Path.GetFileName(entry.RelativePath);
            try
            {
                string scanPath = Path.Combine(workDir, entry.RelativePath);
                using var engine = new PhotoCropperEngine(scanPath);
                engine.ApplyOptions(entry.CustomOptions ?? detection);

                ScanOutcome outcome;
                if (entry.HasSavedCrops)
                {
                    // Boxes saved by the app (possibly edited, possibly none left) are what the user wants.
                    engine.RestoreFromSavedCrops(entry.FinalCrops);
                    outcome = ScanOutcome.Saved;
                }
                else
                {
                    int count = Detect(engine, options);
                    outcome = count == 0 || count < options.MinExpectedPhotos || count > options.MaxExpectedPhotos
                        ? ScanOutcome.Flagged
                        : ScanOutcome.Detected;
                }

                var crops = WorkspaceCropData.FromEngine(engine);
                int photos = Math.Min(engine.RawDetectedPhotos.Count, crops.Count);
                var inputs = Enumerable.Range(0, photos).Select(p => new CroppedPhotoInput(engine.RawDetectedPhotos[p], crops[p])).ToList();
                PhotoPipelineService.SaveCroppedPhotos(workDir, session, entry, inputs, restoration);

                lock (session)
                {
                    // Saved boxes are kept by the app and later runs. A detection that found nothing isn't saved,
                    // so the scan is tried again (and still flagged) rather than kept empty as if reviewed.
                    if (outcome == ScanOutcome.Saved || crops.Count > 0) entry.SetFinalCrops(crops);
                    entry.IsProcessed = true;
                    entry.ExtractedPhotoCount = photos;
                }

                results[i] = new ScanResult(name, outcome, photos);
                if (options.Verbose || outcome == ScanOutcome.Flagged)
                {
                    string tag = outcome switch
                    {
                        ScanOutcome.Saved => "[blue]= saved boxes[/]",
                        ScanOutcome.Flagged => "[yellow]⚠ check[/]",
                        _ => "[green]✓ detected[/]"
                    };
                    console.MarkupLine($"  {tag} [white]{Markup.Escape(name)}[/] → {photos} photo(s)");
                }
            }
            catch (Exception ex)
            {
                results[i] = new ScanResult(name, ScanOutcome.Failed, 0, ex.Message);
                console.MarkupLine($"  [red]✗[/] [white]{Markup.Escape(name)}[/]: {Markup.Escape(ex.Message)}");
            }
        });

        return [.. Enumerable.Range(0, entries.Count).Select(i => results[i])];
    }

    /// <summary>Detects the scan's photos, auto-tuning as the options ask; returns the photo count.</summary>
    private static int Detect(PhotoCropperEngine engine, CliOptions options)
    {
        engine.DetectPhotos();
        int count = engine.DetectedPhotos.Count;
        bool outOfRange = count < options.MinExpectedPhotos || count > options.MaxExpectedPhotos;
        // Like the app: unless an auto-tune option says otherwise, scans with little of their area detected are auto-tuned.
        bool adjustLowCoverage = options.AutoAdjustLowCoverage || (!options.AutoTune && !options.AlwaysAutoTune && !options.AutoTuneOff);
        bool lowCoverage = adjustLowCoverage && engine.TotalDetectedAreaRatio < options.MinCoverageThresholdPercent / 100.0;
        if (options.AlwaysAutoTune || (options.AutoTune && outOfRange) || lowCoverage)
        {
            count = engine.AutoTune(options.MinExpectedPhotos, options.MaxExpectedPhotos).PhotoCount;
        }
        return count;
    }

    private static void PrintHeader(CliOptions options, string workDir)
    {
        var grid = new Grid();
        grid.AddColumn(new GridColumn().PadRight(2));
        grid.AddColumn(new GridColumn());
        grid.AddRow("[bold cyan]Project:[/]", Markup.Escape(workDir));
        grid.AddRow("[bold cyan]Stage:[/]", options.Stage switch
        {
            PipelineStage.Crop => "[bold yellow]Crop only[/] (RawScans → Cropped)",
            PipelineStage.Restore => "[bold yellow]Finish only[/] (Cropped → Final)",
            _ => "All (RawScans → Cropped → Final)"
        });
        if (options.Inputs.Count > 0) grid.AddRow("[bold cyan]Adding:[/]", Markup.Escape(string.Join(", ", options.Inputs)));
        grid.AddRow("[bold cyan]Output Format:[/]", $"{options.Format} (Quality: {options.JpegQuality})");
        AnsiConsole.Write(new Panel(grid).Header("[bold cyan]PhotoCropper CLI - Project[/]").Border(BoxBorder.Rounded).BorderColor(Color.Cyan1));
    }

    private static void PrintSummary(ProjectResult result, string workDir, CliOptions options, TimeSpan elapsed)
    {
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Cyan1)
            .AddColumn("[bold]Metric[/]").AddColumn("[bold]Result[/]");
        table.AddRow("Scans Added", $"{result.Added}");
        if (options.Stage != PipelineStage.Restore)
        {
            table.AddRow("Scans Cropped", $"{result.Scans.Count}");
            table.AddRow("  with saved boxes", $"{result.Scans.Count(s => s.Outcome == ScanOutcome.Saved)}");
            table.AddRow("  detected", $"{result.Scans.Count(s => s.Outcome is ScanOutcome.Detected or ScanOutcome.Flagged)}");
            int flagged = result.Scans.Count(s => s.Outcome == ScanOutcome.Flagged);
            table.AddRow("To Check in the App", flagged == 0 ? "[green]0[/]" : $"[bold yellow]{flagged}[/]");
            table.AddRow("Photos", $"[bold green]{result.Photos}[/]");
        }
        if (options.Stage != PipelineStage.Crop) table.AddRow("Final Photos Written", $"[bold green]{result.FinalsWritten}[/]");
        table.AddRow("Errors", result.Failed == 0 ? "[green]0[/]" : $"[bold red]{result.Failed}[/]");
        table.AddRow("Total Time", $"{elapsed.TotalSeconds.ToString("0.00", CultureInfo.CurrentCulture)}s");
        AnsiConsole.Write(new Panel(table).Header("[bold cyan]Project Summary[/]").Border(BoxBorder.Rounded));
        AnsiConsole.MarkupLine($"[grey]Open {Markup.Escape(workDir)} in the PhotoCropper app to review the crops.[/]");
    }
}
