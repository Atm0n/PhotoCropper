using OpenCvSharp;
using PhotoCropper.Cli.Models;
using PhotoCropper.Cli.Parsing;
using PhotoCropper.Cli.Services;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("PhotoCropper.Cli.Tests")]

namespace PhotoCropper.Cli;

internal static class Program
{
    internal static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        Cv2.SetLogLevel(LogLevel.ERROR);
        Environment.SetEnvironmentVariable("OPENCV_LOG_LEVEL", "ERROR");
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        if (args.Contains("-h") || args.Contains("--help"))
        {
            CommandLineParser.PrintHelp();
            return 0;
        }

        if (args.Contains("--version"))
        {
            CommandLineParser.PrintVersion();
            return 0;
        }

        if (args.Contains("--check-update"))
        {
            return PerformUpdateCheck();
        }

        CliOptions options = CommandLineParser.Parse(args);

        if (options.Interactive || (args.Length == 0 && !Console.IsInputRedirected))
        {
            var wizardOptions = InteractiveWizard.Run(Spectre.Console.AnsiConsole.Console, options);
            if (wizardOptions == null)
            {
                return 0;
            }
            options = wizardOptions;
        }
        else if (args.Length == 0)
        {
            CommandLineParser.PrintHelp();
            return 0;
        }

        // A project has its own scans: input files are optional and are added to it.
        if (options.ProjectDirectory != null)
        {
            return ProjectProcessor.Execute(options);
        }

        if (options.Inputs.Count == 0)
        {
            Spectre.Console.AnsiConsole.MarkupLine("[bold red]Error:[/] No input files or directories specified.");
            Spectre.Console.AnsiConsole.MarkupLine("[grey]Use --help for usage instructions or -w / --wizard for guided setup.[/]");
            return 1;
        }

        List<string> scanFiles = [.. PhotoCropper.Core.IO.ImageFileCollector.CollectFiles(options.Inputs, options.Recursive)];
        if (scanFiles.Count == 0)
        {
            Spectre.Console.AnsiConsole.MarkupLine("[bold yellow]Warning:[/] No supported image files found in specified inputs.");
            return 0;
        }

        return options.Stage == PhotoCropper.Cli.Models.PipelineStage.Restore
            ? RestoreProcessor.Execute(options, scanFiles)
            : BatchProcessor.Execute(options, scanFiles);
    }

    private static int PerformUpdateCheck()
    {
        Spectre.Console.AnsiConsole.MarkupLine("[grey]Checking for updates...[/]");
        var result = PhotoCropper.Core.Updates.UpdateCheckService.CheckForUpdateAsync().GetAwaiter().GetResult();

        if (result.IsUpdateAvailable && result.LatestVersion != null && result.Tag != null && result.ReleaseUri != null)
        {
            var panel = new Spectre.Console.Panel(new Spectre.Console.Markup(
                $"[bold yellow]⚡ A new version is available![/]\n\n" +
                $"Current Version: [white]v{result.CurrentVersion?.Major}.{result.CurrentVersion?.Minor}.{Math.Max(0, result.CurrentVersion?.Build ?? 0)}[/]\n" +
                $"Latest Version:  [bold green]{result.Tag}[/]\n\n" +
                $"Release page & downloads:\n[link]{result.ReleaseUri}[/]"))
            {
                Header = new Spectre.Console.PanelHeader(" PhotoCropper CLI - Update Check ", Spectre.Console.Justify.Left),
                Border = Spectre.Console.BoxBorder.Rounded
            };
            Spectre.Console.AnsiConsole.Write(panel);
        }
        else if (result.ErrorMessage != null)
        {
            Spectre.Console.AnsiConsole.MarkupLine($"[bold yellow]⚠ Could not check for updates:[/] {result.ErrorMessage}");
        }
        else
        {
            var cur = result.CurrentVersion ?? PhotoCropper.Core.Updates.UpdateCheckService.GetCurrentVersion();
            Spectre.Console.AnsiConsole.MarkupLine($"[bold green]✓[/] PhotoCropper CLI is up to date (v{cur.Major}.{cur.Minor}.{Math.Max(0, cur.Build)}).");
        }

        return 0;
    }
}
