using PhotoCropper.Cli.Models;
using PhotoCropper.Core.Export;
using Spectre.Console;
using System.Globalization;

namespace PhotoCropper.Cli.Services;

internal static class InteractiveWizard
{
    public static CliOptions? Run(IAnsiConsole console, CliOptions? initial = null)
    {
        ArgumentNullException.ThrowIfNull(console);

        var options = initial != null ? CloneOptions(initial) : new CliOptions();

        console.Clear();
        console.Write(
            new FigletText("PhotoCropper")
                .Centered()
                .Color(Color.Cyan1));

        console.Write(new Rule("[bold cyan]Interactive Batch Extractor Wizard[/]").RuleStyle("grey"));
        console.MarkupLine("[grey]Follow the prompts below to configure and run photo extraction.[/]\n");

        // 1. Input Selection
        List<string> scanFiles = ConfigureInputs(console, options);
        if (scanFiles.Count == 0)
        {
            console.MarkupLine("[yellow]No input files provided. Wizard cancelled.[/]");
            return null;
        }

        // 2. Output Directory
        ConfigureOutputDirectory(console, options, scanFiles);

        // 3. Format & Quality
        ConfigureFormatAndQuality(console, options);

        // 4. AI & Enhancements
        ConfigureEnhancements(console, options);

        // 5. Detection Settings & Auto-Tune
        ConfigureDetection(console, options);

        // 6. Filename Pattern & EXIF Metadata
        ConfigureNamingAndMetadata(console, options);

        // 7. Performance / Threads
        ConfigurePerformance(console, options);

        // 8. Confirmation Summary
        DisplaySummary(console, options, scanFiles.Count);

        bool confirmed = console.Prompt(
            new ConfirmationPrompt("\n[bold green]Start batch extraction now?[/]")
            {
                DefaultValue = true
            });

        if (!confirmed)
        {
            console.MarkupLine("[yellow]Batch extraction cancelled by user.[/]");
            return null;
        }

        return options;
    }

    private static List<string> ConfigureInputs(IAnsiConsole console, CliOptions options)
    {
        while (true)
        {
            string inputPrompt = options.Inputs.Count > 0
                ? $"[bold]Scan file or directory path[/] [grey](current: {options.Inputs[0]}):[/]"
                : "[bold]Enter scan image file or directory path:[/]";

            string rawPath = console.Prompt(
                new TextPrompt<string>(inputPrompt)
                    .DefaultValue(options.Inputs.Count > 0 ? options.Inputs[0] : string.Empty)
                    .AllowEmpty());

            string cleanedPath = rawPath.Trim('"', '\'', ' ');

            if (string.IsNullOrWhiteSpace(cleanedPath))
            {
                if (options.Inputs.Count > 0)
                {
                    cleanedPath = options.Inputs[0];
                }
                else
                {
                    console.MarkupLine("[red]Path cannot be empty. Please enter a valid path.[/]");
                    continue;
                }
            }

            if (!File.Exists(cleanedPath) && !Directory.Exists(cleanedPath))
            {
                console.MarkupLine($"[red]Error:[/] '{cleanedPath}' does not exist on disk. Please try again.");
                continue;
            }

            options.Inputs.Clear();
            options.Inputs.Add(cleanedPath);

            if (Directory.Exists(cleanedPath))
            {
                options.Recursive = console.Prompt(
                    new ConfirmationPrompt("[bold]Include subdirectories recursively?[/]")
                    {
                        DefaultValue = options.Recursive
                    });
            }

            List<string> collected = FileCollector.CollectFiles(options.Inputs, options.Recursive);
            if (collected.Count == 0)
            {
                console.MarkupLine($"[bold yellow]Warning:[/] No supported image files (.jpg, .png, .bmp, .tiff, .webp) found in '{cleanedPath}'.");
                bool retry = console.Prompt(new ConfirmationPrompt("Would you like to specify a different path?") { DefaultValue = true });
                if (!retry) return [];
                continue;
            }

            console.MarkupLine($"[bold green]✓ Found {collected.Count} scan file(s) ready for extraction.[/]\n");
            return collected;
        }
    }

    private static void ConfigureOutputDirectory(IAnsiConsole console, CliOptions options, List<string> scanFiles)
    {
        string defaultDir;
        if (!string.IsNullOrWhiteSpace(options.OutputDirectory))
        {
            defaultDir = options.OutputDirectory;
        }
        else if (options.Inputs.Count > 0 && Directory.Exists(options.Inputs[0]))
        {
            defaultDir = Path.Combine(options.Inputs[0], "cropped");
        }
        else if (scanFiles.Count > 0)
        {
            string? parent = Path.GetDirectoryName(scanFiles[0]);
            defaultDir = Path.Combine(string.IsNullOrEmpty(parent) ? "." : parent, "cropped");
        }
        else
        {
            defaultDir = Path.Combine(Environment.CurrentDirectory, "cropped");
        }

        string rawOut = console.Prompt(
            new TextPrompt<string>("[bold]Destination directory for cropped photos:[/]")
                .DefaultValue(defaultDir));

        options.OutputDirectory = rawOut.Trim('"', '\'', ' ');
        console.WriteLine();
    }

    private static void ConfigureFormatAndQuality(IAnsiConsole console, CliOptions options)
    {
        string formatChoice = console.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold]Select output image format:[/]")
                .AddChoices(
                    "JPEG (Recommended - compressed, standard photo library format)",
                    "PNG  (Lossless - highest quality, larger file size)"));

        if (formatChoice.StartsWith("JPEG", StringComparison.OrdinalIgnoreCase))
        {
            options.Format = "JPEG";
            options.JpegQuality = console.Prompt(
                new TextPrompt<int>("[bold]JPEG Compression Quality (1-100):[/]")
                    .DefaultValue(options.JpegQuality)
                    .Validate(q => q is >= 1 and <= 100
                        ? ValidationResult.Success()
                        : ValidationResult.Error("Quality must be an integer between 1 and 100.")));
        }
        else
        {
            options.Format = "PNG";
        }

        console.WriteLine();
    }

    private static void ConfigureEnhancements(IAnsiConsole console, CliOptions options)
    {
        console.MarkupLine("[bold cyan]Image Processing & AI Enhancements[/]");

        options.AutoOrient = console.Prompt(
            new ConfirmationPrompt("  Enable AI Face & Landscape auto-orientation?")
            {
                DefaultValue = options.AutoOrient
            });

        options.RestoreColors = console.Prompt(
            new ConfirmationPrompt("  Enable Vintage Color Restoration (Auto-WB + CLAHE dynamic contrast)?")
            {
                DefaultValue = options.RestoreColors
            });

        options.RemoveDust = console.Prompt(
            new ConfirmationPrompt("  Enable Automated Dust, Hair & Scratch inpainting?")
            {
                DefaultValue = options.RemoveDust
            });

        console.WriteLine();
    }

    private static void ConfigureDetection(IAnsiConsole console, CliOptions options)
    {
        console.MarkupLine("[bold cyan]Detection Sensitivity[/]");

        string profile = console.Prompt(
            new SelectionPrompt<string>()
                .Title("  [bold]Choose detection sensitivity profile:[/]")
                .AddChoices(
                    "Standard    - Tolerance: 25, Min Size: 25% (Recommended for most flatbed scans)",
                    "Sensitive   - Tolerance: 35, Min Size: 15% (For light borders or faint separation)",
                    "Aggressive  - Tolerance: 45, Min Size: 10% (For dark scanner lids or irregular photos)",
                    "Custom      - Manually configure detection parameters"));

        if (profile.StartsWith("Standard", StringComparison.OrdinalIgnoreCase))
        {
            options.Tolerance = 25.0;
            options.MinAreaFactor = 0.25;
            options.MaxAreaFactor = 0.90;
            options.CannyLow = 20.0;
        }
        else if (profile.StartsWith("Sensitive", StringComparison.OrdinalIgnoreCase))
        {
            options.Tolerance = 35.0;
            options.MinAreaFactor = 0.15;
            options.MaxAreaFactor = 0.95;
            options.CannyLow = 15.0;
        }
        else if (profile.StartsWith("Aggressive", StringComparison.OrdinalIgnoreCase))
        {
            options.Tolerance = 45.0;
            options.MinAreaFactor = 0.10;
            options.MaxAreaFactor = 0.95;
            options.CannyLow = 15.0;
        }
        else
        {
            options.Tolerance = console.Prompt(
                new TextPrompt<double>("    Background Color Tolerance (5-80):")
                    .DefaultValue(options.Tolerance)
                    .Validate(t => t is >= 5 and <= 80 ? ValidationResult.Success() : ValidationResult.Error("Tolerance must be between 5 and 80.")));

            double minPct = console.Prompt(
                new TextPrompt<double>("    Minimum photo size as % of scan area (1-50%):")
                    .DefaultValue(options.MinAreaFactor * 100.0)
                    .Validate(m => m is >= 1 and <= 50 ? ValidationResult.Success() : ValidationResult.Error("Minimum size must be between 1% and 50%.")));
            options.MinAreaFactor = minPct / 100.0;
        }

        options.MinExpectedPhotos = console.Prompt(
            new TextPrompt<int>("  [bold]Minimum expected photos per scan bed (flag scans with fewer):[/]")
                .DefaultValue(options.MinExpectedPhotos)
                .Validate(p => p >= 1 ? ValidationResult.Success() : ValidationResult.Error("Must be at least 1 photo.")));

        string maxDefault = options.MaxExpectedPhotos == int.MaxValue ? "" : options.MaxExpectedPhotos.ToString(CultureInfo.InvariantCulture);
        string maxPromptInput = console.Prompt(
            new TextPrompt<string>("  [bold]Maximum expected photos per scan bed (press Enter for unlimited):[/]")
                .DefaultValue(maxDefault)
                .AllowEmpty()
                .Validate(val =>
                {
                    if (string.IsNullOrWhiteSpace(val)) return ValidationResult.Success();
                    if (int.TryParse(val.Trim(), CultureInfo.InvariantCulture, out int maxP) && maxP >= options.MinExpectedPhotos)
                    {
                        return ValidationResult.Success();
                    }
                    return ValidationResult.Error($"Must be an integer greater than or equal to minimum ({options.MinExpectedPhotos}).");
                }));

        options.MaxExpectedPhotos = string.IsNullOrWhiteSpace(maxPromptInput)
            ? int.MaxValue
            : int.Parse(maxPromptInput.Trim(), CultureInfo.InvariantCulture);

        string autoTunePromptText = options.MaxExpectedPhotos == int.MaxValue
            ? $"  Enable ⚡ Auto-Tune sweep on scans where fewer than {options.MinExpectedPhotos} photo(s) are detected?"
            : $"  Enable ⚡ Auto-Tune sweep on scans outside expected range ({options.MinExpectedPhotos}-{options.MaxExpectedPhotos} photos)?";

        options.AutoTune = console.Prompt(
            new ConfirmationPrompt(autoTunePromptText)
            {
                DefaultValue = options.AutoTune
            });

        console.WriteLine();
    }

    private static void ConfigureNamingAndMetadata(IAnsiConsole console, CliOptions options)
    {
        // 1. Vintage EXIF Metadata
        console.MarkupLine("[bold cyan]Vintage EXIF Metadata[/]");
        bool addExif = console.Prompt(
            new ConfirmationPrompt("  Embed vintage EXIF capture metadata (Year / Date / Description)?")
            {
                DefaultValue = options.Year.HasValue || !string.IsNullOrWhiteSpace(options.Date) || !string.IsNullOrWhiteSpace(options.Description)
            });

        if (addExif)
        {
            string yearInput = console.Prompt(
                new TextPrompt<string>("    Vintage photo year (e.g., 1982, press Enter to skip):")
                    .DefaultValue(options.Year.HasValue ? options.Year.Value.ToString(CultureInfo.InvariantCulture) : string.Empty)
                    .AllowEmpty());

            if (int.TryParse(yearInput.Trim(), CultureInfo.InvariantCulture, out int yr))
            {
                options.Year = yr;
            }
            else
            {
                options.Year = null;
            }

            string dateInput = console.Prompt(
                new TextPrompt<string>("    Approximate or exact capture date (YYYY-MM-DD, press Enter to skip):")
                    .DefaultValue(options.Date ?? string.Empty)
                    .AllowEmpty()
                    .Validate(val =>
                    {
                        if (string.IsNullOrWhiteSpace(val)) return ValidationResult.Success();
                        string trimmed = val.Trim();
                        if (DateTime.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                            || DateTime.TryParseExact(trimmed, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                            || DateTime.TryParseExact(trimmed, "yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                        {
                            return ValidationResult.Success();
                        }
                        return ValidationResult.Error("Date must be in valid format (YYYY-MM-DD, YYYY-MM, or YYYY).");
                    }));

            options.Date = string.IsNullOrWhiteSpace(dateInput) ? null : dateInput.Trim();

            string descInput = console.Prompt(
                new TextPrompt<string>("    Description / Album name (press Enter to skip):")
                    .DefaultValue(options.Description ?? string.Empty)
                    .AllowEmpty());

            options.Description = string.IsNullOrWhiteSpace(descInput) ? null : descInput.Trim();
        }

        console.WriteLine();

        // 2. Output Filename Pattern (incorporates Year & EXIF values)
        console.MarkupLine("[bold cyan]Output File Naming Pattern[/]");

        DateTime? parsedDate = null;
        if (!string.IsNullOrWhiteSpace(options.Date) && DateTime.TryParse(options.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            parsedDate = d;
        }

        var sampleMeta = new PhotoCropper.Core.Models.PhotoExportMetadata
        {
            Year = options.Year,
            DateTaken = parsedDate,
            Description = options.Description
        };

        string ext = string.Equals(options.Format, "PNG", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
        string sampleOriginal = options.Inputs.Count > 0 && File.Exists(options.Inputs[0])
            ? Path.GetFileNameWithoutExtension(options.Inputs[0])
            : "scan01";

        var choices = new List<string>();
        if (options.Year.HasValue)
        {
            string yearOriginalPreview = FileNameTemplateHelper.FormatPreview(FileNameTemplateHelper.YearOriginalPattern, sampleOriginal, 1, 4, sampleMeta, ext);
            choices.Add($"{FileNameTemplateHelper.YearOriginalPattern,-30} (e.g., {yearOriginalPreview}) [[Recommended]]");

            string yearIndexPreview = FileNameTemplateHelper.FormatPreview(FileNameTemplateHelper.YearIndexPattern, sampleOriginal, 1, 4, sampleMeta, ext);
            choices.Add($"{FileNameTemplateHelper.YearIndexPattern,-30} (e.g., {yearIndexPreview})");
        }

        string defaultPreview = FileNameTemplateHelper.FormatPreview(FileNameTemplateHelper.DefaultPattern, sampleOriginal, 1, 4, sampleMeta, ext);
        choices.Add($"{FileNameTemplateHelper.DefaultPattern,-30} (e.g., {defaultPreview})");

        string zeroPaddedPreview = FileNameTemplateHelper.FormatPreview(FileNameTemplateHelper.ZeroPaddedPattern, sampleOriginal, 1, 4, sampleMeta, ext);
        choices.Add($"{FileNameTemplateHelper.ZeroPaddedPattern,-30} (e.g., {zeroPaddedPreview})");

        if (!options.Year.HasValue)
        {
            string yearPreview = FileNameTemplateHelper.FormatPreview(FileNameTemplateHelper.YearOriginalPattern, sampleOriginal, 1, 4, sampleMeta, ext);
            choices.Add($"{FileNameTemplateHelper.YearOriginalPattern,-30} (e.g., {yearPreview})");
        }

        choices.Add("Custom Pattern...");

        string patternChoice = console.Prompt(
            new SelectionPrompt<string>()
                .Title("  [bold]Choose output filename pattern:[/]")
                .AddChoices(choices));

        if (patternChoice.StartsWith("Custom", StringComparison.OrdinalIgnoreCase))
        {
            options.FileNamePattern = console.Prompt(
                new TextPrompt<string>("    Enter custom filename template (Tokens: {original}, {index}, {index:02}, {year}, {date}, {total}):")
                    .DefaultValue(options.FileNamePattern));
        }
        else
        {
            options.FileNamePattern = patternChoice.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        }

        string finalPreview = FileNameTemplateHelper.FormatPreview(options.FileNamePattern, sampleOriginal, 1, 4, sampleMeta, ext);
        console.MarkupLine($"  [grey]Live Preview:[/] [bold green]{Markup.Escape(finalPreview)}[/]\n");
    }

    private static void ConfigurePerformance(IAnsiConsole console, CliOptions options)
    {
        int defaultThreads = options.Threads > 0 ? options.Threads : Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

        options.Threads = console.Prompt(
            new TextPrompt<int>("[bold]Parallel CPU worker threads:[/]")
                .DefaultValue(defaultThreads)
                .Validate(t => t >= 1 ? ValidationResult.Success() : ValidationResult.Error("Threads must be at least 1.")));

        console.WriteLine();
    }

    private static void DisplaySummary(IAnsiConsole console, CliOptions options, int fileCount)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .AddColumn(new TableColumn("[bold cyan]Parameter[/]").Width(24))
            .AddColumn(new TableColumn("[bold cyan]Configured Value[/]"));

        table.AddRow("Total Scans", $"[bold white]{fileCount}[/] file(s)");
        table.AddRow("Input Source", Markup.Escape(options.Inputs[0]) + (options.Recursive ? " [grey](recursive)[/]" : ""));
        table.AddRow("Output Directory", $"[bold white]{Markup.Escape(options.OutputDirectory ?? "cropped")}[/]");
        table.AddRow("Output Format", options.Format == "JPEG" ? $"[bold white]JPEG[/] (Quality: {options.JpegQuality})" : "[bold white]PNG[/] (Lossless)");
        table.AddRow("Detection Profile", $"Tolerance: {options.Tolerance:0}, MinSize: {options.MinAreaFactor * 100:0}%, MaxSize: {options.MaxAreaFactor * 100:0}%");
        table.AddRow("Min Expected Photos", $"{options.MinExpectedPhotos} photo(s) per scan");
        table.AddRow("Max Expected Photos", options.MaxExpectedPhotos == int.MaxValue ? "[grey]Unlimited[/]" : $"{options.MaxExpectedPhotos} photo(s) per scan");
        table.AddRow("⚡ Auto-Tune Sweep", options.AutoTune ? "[bold green]Enabled[/]" : "[grey]Disabled[/]");
        table.AddRow("AI Auto-Orientation", options.AutoOrient ? "[bold green]Enabled[/]" : "[grey]Disabled[/]");
        table.AddRow("Color Restoration", options.RestoreColors ? "[bold green]Enabled[/]" : "[grey]Disabled[/]");
        table.AddRow("Dust/Scratch Removal", options.RemoveDust ? "[bold green]Enabled[/]" : "[grey]Disabled[/]");
        table.AddRow("Naming Pattern", $"[bold yellow]{Markup.Escape(options.FileNamePattern)}[/]");

        if (options.Year.HasValue || !string.IsNullOrWhiteSpace(options.Date) || !string.IsNullOrWhiteSpace(options.Description))
        {
            string meta = "";
            if (options.Year.HasValue) meta += $"Year: {options.Year} ";
            if (!string.IsNullOrWhiteSpace(options.Date)) meta += $"Date: {options.Date} ";
            if (!string.IsNullOrWhiteSpace(options.Description)) meta += $"Desc: '{options.Description}'";
            table.AddRow("Vintage EXIF", $"[bold green]{Markup.Escape(meta.Trim())}[/]");
        }

        table.AddRow("Worker Threads", $"{options.Threads} CPU threads");

        console.Write(new Panel(table).Header("[bold cyan]Batch Job Configuration Summary[/]").Border(BoxBorder.Rounded));
    }

    private static CliOptions CloneOptions(CliOptions source)
    {
        var clone = new CliOptions
        {
            OutputDirectory = source.OutputDirectory,
            Format = source.Format,
            JpegQuality = source.JpegQuality,
            Tolerance = source.Tolerance,
            MinAreaFactor = source.MinAreaFactor,
            MaxAreaFactor = source.MaxAreaFactor,
            CannyLow = source.CannyLow,
            AutoOrient = source.AutoOrient,
            RestoreColors = source.RestoreColors,
            RemoveDust = source.RemoveDust,
            Threads = source.Threads,
            Recursive = source.Recursive,
            Verbose = source.Verbose,
            AutoTune = source.AutoTune,
            CopyUndetectedDirectory = source.CopyUndetectedDirectory,
            NonInteractive = source.NonInteractive,
            Interactive = source.Interactive,
            MinExpectedPhotos = source.MinExpectedPhotos,
            MaxExpectedPhotos = source.MaxExpectedPhotos,
            FileNamePattern = source.FileNamePattern,
            Year = source.Year,
            Date = source.Date,
            Description = source.Description
        };

        foreach (var input in source.Inputs)
        {
            clone.Inputs.Add(input);
        }

        return clone;
    }
}
