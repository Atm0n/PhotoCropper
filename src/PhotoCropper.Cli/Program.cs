using PhotoCropperCli.Models;
using PhotoCropperCli.Parsing;
using PhotoCropperCli.Services;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

[assembly: SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters", Justification = "CLI application outputs standard console messages")]
[assembly: InternalsVisibleTo("PhotoCropper.Tests")]

namespace PhotoCropperCli;

internal static class Program
{
    internal static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
        {
            CommandLineParser.PrintHelp();
            return 0;
        }

        if (args.Contains("--version"))
        {
            CommandLineParser.PrintVersion();
            return 0;
        }

        CliOptions options = CommandLineParser.Parse(args);
        if (options.Inputs.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: No input files or directories specified.");
            Console.ResetColor();
            Console.WriteLine("Use --help for usage instructions.");
            return 1;
        }

        List<string> scanFiles = FileCollector.CollectFiles(options.Inputs, options.Recursive);
        if (scanFiles.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Warning: No supported image files found in specified inputs.");
            Console.ResetColor();
            return 0;
        }

        return BatchProcessor.Execute(options, scanFiles);
    }
}
