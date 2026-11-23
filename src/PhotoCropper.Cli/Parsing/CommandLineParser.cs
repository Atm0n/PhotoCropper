using PhotoCropper.Cli.Models;
using System.Globalization;

namespace PhotoCropper.Cli.Parsing;

internal static class CommandLineParser
{
    public static CliOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var options = new CliOptions();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (arg is "-o" or "--output" && i + 1 < args.Length)
            {
                options.OutputDirectory = args[++i];
            }
            else if (arg is "-f" or "--format" && i + 1 < args.Length)
            {
                options.Format = args[++i].ToUpperInvariant();
            }
            else if (arg is "-q" or "--quality" && i + 1 < args.Length)
            {
                if (TryInt(arg, args[++i], out int q))
                {
                    options.JpegQuality = Math.Clamp(q, 1, 100);
                }
            }
            else if (arg is "-s" or "--sensitivity" && i + 1 < args.Length)
            {
                if (TryDouble(arg, args[++i], out double sens))
                {
                    options.Tolerance = PhotoCropper.Core.Models.DetectionOptions.SensitivityToTolerance(sens);
                }
            }
            else if (arg is "-t" or "--tolerance" && i + 1 < args.Length)
            {
                if (TryDouble(arg, args[++i], out double tol))
                {
                    options.Tolerance = tol;
                }
            }
            else if (arg == "--min-size" && i + 1 < args.Length)
            {
                if (TryDouble(arg, args[++i], out double minSize))
                {
                    options.MinAreaFactor = minSize / 100.0;
                }
            }
            else if (arg == "--max-size" && i + 1 < args.Length)
            {
                if (TryDouble(arg, args[++i], out double maxSize))
                {
                    options.MaxAreaFactor = maxSize / 100.0;
                }
            }
            else if (arg == "--canny-low" && i + 1 < args.Length)
            {
                if (TryDouble(arg, args[++i], out double canny))
                {
                    options.CannyLow = canny;
                }
            }
            else if (arg is "-j" or "--threads" && i + 1 < args.Length)
            {
                if (TryInt(arg, args[++i], out int th))
                {
                    options.Threads = Math.Max(1, th);
                }
            }
            else if (arg is "--min-photos" or "--expected-photos" && i + 1 < args.Length)
            {
                if (TryInt(arg, args[++i], out int mp))
                {
                    options.MinExpectedPhotos = Math.Max(1, mp);
                }
            }
            else if (arg is "--max-photos" or "--max-expected-photos" && i + 1 < args.Length)
            {
                if (TryInt(arg, args[++i], out int maxP))
                {
                    options.MaxExpectedPhotos = Math.Max(1, maxP);
                }
            }
            else if (arg is "-r" or "--recursive")
            {
                options.Recursive = true;
            }
            else if (arg is "-v" or "--verbose")
            {
                options.Verbose = true;
            }
            else if (arg == "--no-orient")
            {
                options.AutoOrient = false;
            }
            else if (arg == "--no-restore")
            {
                options.RestoreColors = false;
            }
            else if (arg == "--no-dust")
            {
                options.RemoveDust = false;
            }
            else if (arg == "--trim-borders")
            {
                options.TrimBorders = true;
            }
            else if (arg == "--auto-orient")
            {
                options.AutoOrient = true;
            }
            else if (arg == "--no-auto-orient")
            {
                options.AutoOrient = false;
            }
            else if (arg == "--restore-colors")
            {
                options.RestoreColors = true;
            }
            else if (arg == "--no-restore-colors")
            {
                options.RestoreColors = false;
            }
            else if (arg == "--remove-dust")
            {
                options.RemoveDust = true;
            }
            else if (arg == "--no-remove-dust")
            {
                options.RemoveDust = false;
            }
            else if (arg == "--auto-tune")
            {
                // Optional mode; a bare --auto-tune keeps its old meaning (tune scans outside the expected count).
                string? given = i + 1 < args.Length
                    ? AutoTuneModes.FirstOrDefault(m => string.Equals(m, args[i + 1], StringComparison.OrdinalIgnoreCase))
                    : null;
                if (given != null) i++;
                string mode = given ?? "mismatch";
                options.AutoTune = mode == "mismatch";
                options.AlwaysAutoTune = mode == "always";
                options.AutoAdjustLowCoverage = mode == "low-coverage";
                options.AutoTuneOff = mode == "off";
            }
            else if (arg == "--always-auto-tune")
            {
                options.AlwaysAutoTune = true;
            }
            else if (arg == "--auto-adjust-low-coverage")
            {
                options.AutoAdjustLowCoverage = true;
            }
            else if (arg == "--min-coverage" && i + 1 < args.Length)
            {
                if (TryDouble(arg, args[++i], out double cov))
                {
                    options.MinCoverageThresholdPercent = Math.Clamp(cov, 1.0, 99.0);
                    options.AutoAdjustLowCoverage = true;
                }
            }
            else if (arg is "--copy-undetected" or "--isolate-undetected" && i + 1 < args.Length)
            {
                options.CopyUndetectedDirectory = args[++i];
            }
            else if (arg is "-y" or "--yes" or "--non-interactive")
            {
                options.NonInteractive = true;
            }
            else if (arg is "-w" or "--wizard" or "--interactive")
            {
                options.Interactive = true;
            }
            else if (arg is "-p" or "--pattern" or "--naming-pattern" && i + 1 < args.Length)
            {
                options.FileNamePattern = args[++i];
                options.FileNamePatternSpecified = true;
            }
            else if (arg == "--project" && i + 1 < args.Length)
            {
                options.ProjectDirectory = args[++i];
            }
            else if (arg == "--stage" && i + 1 < args.Length)
            {
                string stage = args[++i];
                if (Enum.TryParse(stage, ignoreCase: true, out Models.PipelineStage parsed) && Enum.IsDefined(parsed))
                {
                    options.Stage = parsed;
                }
                else
                {
                    Spectre.Console.AnsiConsole.MarkupLine($"[bold yellow]Warning:[/] Unknown stage '{Spectre.Console.Markup.Escape(stage)}' (use all, crop or restore); running all stages.");
                }
            }
            else if (arg == "--year" && i + 1 < args.Length)
            {
                if (TryInt(arg, args[++i], out int year))
                {
                    options.Year = year;
                }
            }
            else if (arg == "--date" && i + 1 < args.Length)
            {
                options.Date = args[++i];
            }
            else if (arg is "--desc" or "--description" or "--comment" && i + 1 < args.Length)
            {
                options.Description = args[++i];
            }
            else if (arg is "--photographer" or "--artist" or "--author" && i + 1 < args.Length)
            {
                options.Photographer = args[++i];
            }
            else if (arg is "-i" or "--input" && i + 1 < args.Length)
            {
                options.Inputs.Add(args[++i]);
            }
            else if (arg is not ['-', ..])
            {
                options.Inputs.Add(arg);
            }
            else
            {
                // A typo used to be ignored silently, running with defaults the user didn't mean.
                Warn(IsValueOption(arg) ? $"'{arg}' needs a value; ignored." : $"Unknown option '{arg}' ignored (see --help).");
            }
        }

        return options;
    }

    /// <summary>The modes --auto-tune accepts.</summary>
    internal static readonly string[] AutoTuneModes = ["off", "mismatch", "always", "low-coverage"];

    private static readonly string[] ValueOptions =
    [
        "-o", "--output", "-f", "--format", "-q", "--quality", "-s", "--sensitivity", "-t", "--tolerance", "--min-size",
        "--max-size", "--canny-low", "-j", "--threads", "--min-photos", "--expected-photos", "--max-photos",
        "--max-expected-photos", "--min-coverage", "--copy-undetected", "--isolate-undetected", "-p", "--pattern",
        "--naming-pattern", "--project", "--stage", "--year", "--date", "--desc", "--description", "--comment",
        "--photographer", "--artist", "--author", "-i", "--input"
    ];

    private static bool IsValueOption(string arg) => ValueOptions.Contains(arg, StringComparer.Ordinal);

    private static void Warn(string message) =>
        Spectre.Console.AnsiConsole.MarkupLine($"[bold yellow]Warning:[/] {Spectre.Console.Markup.Escape(message)}");

    private static bool TryInt(string option, string value, out int result)
    {
        if (int.TryParse(value, CultureInfo.InvariantCulture, out result)) return true;
        Warn($"{option} expects a whole number, not '{value}'; ignored.");
        return false;
    }

    private static bool TryDouble(string option, string value, out double result)
    {
        if (double.TryParse(value, CultureInfo.InvariantCulture, out result)) return true;
        Warn($"{option} expects a number, not '{value}'; ignored.");
        return false;
    }

    /// <summary>Prints the command line's help, kept in the embedded Help.txt.</summary>
    public static void PrintHelp()
    {
        using Stream stream = typeof(CommandLineParser).Assembly.GetManifestResourceStream(HelpResource)
            ?? throw new InvalidOperationException($"The help text ({HelpResource}) is missing from the program.");
        using var reader = new StreamReader(stream);
        Console.Write(reader.ReadToEnd());
    }

    private const string HelpResource = "PhotoCropper.Cli.Help.txt";

    public static void PrintVersion()
    {
        var ver = PhotoCropper.Core.Updates.UpdateCheckService.GetCurrentVersion();
        Console.WriteLine($"PhotoCropper CLI v{ver.Major}.{ver.Minor}.{Math.Max(0, ver.Build)} (.NET 11 / OpenCV)");
    }
}
