using PhotoCropperCli.Models;
using System.Globalization;

namespace PhotoCropperCli.Parsing;

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
        Console.WriteLine("PhotoCropper CLI - Automated Multi-Photo Scanner Cropper & Extractor");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  PhotoCropperCli [options] <input-file-or-directory> [more-inputs...]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -i, --input <path>      Input image file or folder of scans (positional arguments also accepted)");
        Console.WriteLine("  -o, --output <dir>      Output directory for extracted photos (default: <scan_dir>/cropped)");
        Console.WriteLine("  -f, --format <fmt>      Output format: JPEG (default) or PNG");
        Console.WriteLine("  -q, --quality <1-100>   JPEG compression quality (default: 90)");
        Console.WriteLine("  -t, --tolerance <num>   Background color detection tolerance (default: 25)");
        Console.WriteLine("  --min-size <percent>    Minimum photo size as % of scan area (default: 15)");
        Console.WriteLine("  --max-size <percent>    Maximum photo size as % of scan area (default: 90)");
        Console.WriteLine("  --canny-low <num>       Canny edge detector sensitivity threshold (default: 20)");
        Console.WriteLine("  --auto-orient           Enable AI face & landscape auto-orientation detection (default: on)");
        Console.WriteLine("  --no-auto-orient        Disable auto-orientation detection and preserve raw scanner placement");
        Console.WriteLine("  --restore-colors        Enable auto-white balance, contrast & vibrancy color restoration (default: on)");
        Console.WriteLine("  --no-restore-colors     Disable color restoration and export raw scanned pixels");
        Console.WriteLine("  --remove-dust           Enable automated scratch and dust inpainting (default: on)");
        Console.WriteLine("  --no-remove-dust        Disable automated scratch and dust inpainting");
        Console.WriteLine("  -r, --recursive         Recursively process subdirectories when input is a folder");
        Console.WriteLine("  -v, --verbose           Display individual photo dimensions and debug details");
        Console.WriteLine("  -h, --help              Show this help message and exit");
        Console.WriteLine("  --version               Show version information");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  PhotoCropperCli scan001.jpg");
        Console.WriteLine("  PhotoCropperCli D:\\Scans -o D:\\Cropped -f PNG -r");
        Console.WriteLine("  PhotoCropperCli -i scan1.jpg -i scan2.jpg -t 30 -q 95");
    }

    public static void PrintVersion()
    {
        Console.WriteLine("PhotoCropper CLI v2.0 (.NET 10 / OpenCV)");
    }
}
