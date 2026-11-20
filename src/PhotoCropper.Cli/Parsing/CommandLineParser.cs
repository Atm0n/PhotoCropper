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

    public static void PrintHelp()
    {
        Console.WriteLine(
            """
            PhotoCropper CLI - finds the photos on scans, crops, straightens and restores them

            Usage:
              PhotoCropperCli <scans...> [options]                     scan files or folders
              PhotoCropperCli --project <folder> [scans...] [options]  an app project
              PhotoCropperCli --wizard                                 guided setup

            Where things go:
              --project <folder>      Work on a PhotoCropper project (created if missing): new scans are added,
                                      boxes saved in the app are kept, and Cropped/ and Final/ are written.
                                      Don't run it on a project that is open in the app.
              -o, --output <dir>      Output folder (default: <scan folder>/cropped; with --project, Final/)
              --stage <stage>         all (default): crop and restore; crop: lossless PNGs, no restoration;
                                      restore: restore existing photos, no detection
              -r, --recursive         Include subfolders of input folders
              -f, --format <fmt>      JPEG (default) or PNG
              -q, --quality <1-100>   JPEG quality (default: 100)
              -p, --pattern <pat>     File names (default: '{original}_{index}'); tokens: {original}, {index},
                                      {index:02}, {year}, {date}, {total}

            Finding photos:
              -s, --sensitivity <0-100>  Detection sensitivity (default: 50)
              --min-size <percent>    Smallest photo, as % of the scan (default: 1, as in the app)
              --max-size <percent>    Largest photo, as % of the scan (default: 90)
              --min-photos <n>        Flag scans with fewer photos (default: 1)
              --max-photos <n>        Flag scans with more photos (default: no limit)
              --auto-tune [mode]      Search for better detection settings: mismatch (scans outside
                                      --min/--max-photos; the mode when none is given), always,
                                      low-coverage or off. Default: low-coverage with --project, else off
              --min-coverage <percent>  What counts as low coverage (default: 50)
              --copy-undetected <dir> Copy scans where no photo was found to this folder

            Photos:
              --no-restore            Don't restore colours (white balance, contrast, faded colours)
              --no-dust               Don't remove dust and scratches
              --trim-borders          Leave out prints' white borders (photos without one are kept whole)
              --no-orient             Don't turn photos upright automatically
              --year <YYYY>           Year taken, written to the photos' metadata and {year}
              --date <YYYY-MM-DD>     Date taken, written to the metadata and {date}
              --desc <text>           Description written to the metadata
              --photographer <name>   Photographer written to the metadata

            Other:
              -j, --threads <n>       Scans processed in parallel (default: half the CPU cores, up to 4)
              -y, --yes               Never ask questions (e.g. whether to auto-tune afterwards)
              -v, --verbose           Show every photo found
              --check-update          Check for a newer version
              --version               Show the version
              -h, --help              Show this help

            Examples:
              PhotoCropperCli D:\Scans -r
              PhotoCropperCli D:\Scans -o D:\Photos -f PNG --auto-tune
              PhotoCropperCli --project D:\Albums\Grandma D:\NewScans
              PhotoCropperCli D:\Scans --stage crop -o D:\Cropped
              PhotoCropperCli D:\OldPhotos --stage restore --year 1975

            Older option names (--tolerance, --canny-low, --always-auto-tune, --no-restore-colors, ...)
            still work.

            Acknowledgements & Licenses:
              PhotoCropper is licensed under GNU General Public License v3.0.
              AI Face Detection uses YuNet (face_detection_yunet_2023mar.onnx)
              developed by Shiqi Yu & OpenCV Zoo (Apache License 2.0).
            """);

    }

    public static void PrintVersion()
    {
        var ver = PhotoCropper.Core.Updates.UpdateCheckService.GetCurrentVersion();
        Console.WriteLine($"PhotoCropper CLI v{ver.Major}.{ver.Minor}.{Math.Max(0, ver.Build)} (.NET 11 / OpenCV)");
    }
}
