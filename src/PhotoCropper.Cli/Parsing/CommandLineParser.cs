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
                if (int.TryParse(args[++i], CultureInfo.InvariantCulture, out int q))
                {
                    options.JpegQuality = Math.Clamp(q, 1, 100);
                }
            }
            else if (arg is "-s" or "--sensitivity" && i + 1 < args.Length)
            {
                if (double.TryParse(args[++i], CultureInfo.InvariantCulture, out double sens))
                {
                    options.Tolerance = PhotoCropper.Core.Models.DetectionOptions.SensitivityToTolerance(sens);
                }
            }
            else if (arg is "-t" or "--tolerance" && i + 1 < args.Length)
            {
                if (double.TryParse(args[++i], CultureInfo.InvariantCulture, out double tol))
                {
                    options.Tolerance = tol;
                }
            }
            else if (arg == "--min-size" && i + 1 < args.Length)
            {
                if (double.TryParse(args[++i], CultureInfo.InvariantCulture, out double minSize))
                {
                    options.MinAreaFactor = minSize / 100.0;
                }
            }
            else if (arg == "--max-size" && i + 1 < args.Length)
            {
                if (double.TryParse(args[++i], CultureInfo.InvariantCulture, out double maxSize))
                {
                    options.MaxAreaFactor = maxSize / 100.0;
                }
            }
            else if (arg == "--canny-low" && i + 1 < args.Length)
            {
                if (double.TryParse(args[++i], CultureInfo.InvariantCulture, out double canny))
                {
                    options.CannyLow = canny;
                }
            }
            else if (arg is "-j" or "--threads" && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], CultureInfo.InvariantCulture, out int th))
                {
                    options.Threads = Math.Max(1, th);
                }
            }
            else if (arg is "--min-photos" or "--expected-photos" && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], CultureInfo.InvariantCulture, out int mp))
                {
                    options.MinExpectedPhotos = Math.Max(1, mp);
                }
            }
            else if (arg is "--max-photos" or "--max-expected-photos" && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], CultureInfo.InvariantCulture, out int maxP))
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
                options.AutoTune = true;
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
            }
            else if (arg == "--year" && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], CultureInfo.InvariantCulture, out int year))
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
            else if (arg is "-i" or "--input" && i + 1 < args.Length)
            {
                options.Inputs.Add(args[++i]);
            }
            else if (!arg.StartsWith('-'))
            {
                options.Inputs.Add(arg);
            }
        }

        return options;
    }

    public static void PrintHelp()
    {
        Console.WriteLine(
            """
            PhotoCropper CLI - Automated Multi-Photo Scanner Cropper & Extractor
        
            Usage:
              PhotoCropperCli [options] <input-file-or-directory> [more-inputs...]
        
            Options:
              -i, --input <path>      Input image file or folder of scans (positional arguments also accepted)
              -o, --output <dir>      Output directory for extracted photos (default: <scan_dir>/cropped)
              -f, --format <fmt>      Output format: JPEG (default) or PNG
              -q, --quality <1-100>   JPEG compression quality (default: 100)
              -p, --pattern <pat>     File naming template (default: '{original}_{index}')
                                      Tokens: {original}, {index}, {index:02}, {year}, {date}, {total}
              --year <YYYY>           Vintage photo year taken to embed in EXIF and use in {year}
              --date <YYYY-MM-DD>     Approximate or exact photo date to embed in EXIF and {date}
              --desc <text>           Photo description/comment embedded into EXIF metadata
              -s, --sensitivity <0-100> Detection sensitivity percentage (default: 50%)
              -t, --tolerance <num>   Background color detection tolerance (default: 25)
              -j, --threads <num>     Number of parallel CPU worker threads (default: CPU core count)
              --min-size <percent>    Minimum photo size as % of scan area (default: 25)
              --max-size <percent>    Maximum photo size as % of scan area (default: 90)
              --min-photos <num>      Minimum expected photos per scan to flag for review (default: 1)
              --max-photos <num>      Maximum expected photos per scan to flag for review (default: unlimited)
              --canny-low <num>       Canny edge detector sensitivity threshold (default: 20)
              --auto-tune             Automatically search optimal detection parameters on difficult scans
              --copy-undetected <dir> Copy undetected scans with 0 photos to a designated review directory
              -w, --wizard            Launch step-by-step interactive CLI wizard
              -y, --non-interactive   Disable interactive prompts (e.g., auto-tune prompts at batch completion)
              --auto-orient           Enable AI face & landscape auto-orientation detection (default: on)
              --no-auto-orient        Disable auto-orientation detection and preserve raw scanner placement
              --restore-colors        Enable auto-white balance, contrast & vibrancy color restoration (default: on)
              --no-restore-colors     Disable color restoration and export raw scanned pixels
              --remove-dust           Enable automated scratch and dust inpainting (default: on)
              --no-remove-dust        Disable automated scratch and dust inpainting
              -r, --recursive         Recursively process subdirectories when input is a folder
              -v, --verbose           Display individual photo dimensions and debug details
              --check-update          Check GitHub for newer versions of PhotoCropper
              -h, --help              Show this help message and exit
              --version               Show version information
        
            Examples:
              PhotoCropperCli scan001.jpg
              PhotoCropperCli D:\Scans -o D:\Cropped -f PNG -r
              PhotoCropperCli -i scan1.jpg -i scan2.jpg -t 30 -q 95
        
            Acknowledgements & Licenses:
              PhotoCropper is licensed under GNU General Public License v3.0.
              AI Face Detection uses YuNet (face_detection_yunet_2023mar.onnx)
              developed by Shiqi Yu & OpenCV Zoo (Apache License 2.0).
            """);

    }

    public static void PrintVersion()
    {
        var ver = PhotoCropper.Core.Updates.UpdateCheckService.GetCurrentVersion();
        Console.WriteLine($"PhotoCropper CLI v{ver.Major}.{ver.Minor}.{Math.Max(0, ver.Build)} (.NET 10 / OpenCV)");
    }
}
