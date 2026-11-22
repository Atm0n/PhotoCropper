using System.Globalization;
using PhotoCropper.Trainer.Commands;
using PhotoCropper.Trainer.Photos;
using Spectre.Console;

namespace PhotoCropper.Trainer;

/// <summary>Reads the command line and runs the command.</summary>
internal static class CommandLine
{
    public static int Run(string[] args, IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(console);
        string command = args.Length == 0 ? "help" : args[0].ToUpperInvariant();
        var options = Options(args.Skip(1));
        try
        {
            return command switch
            {
                "CHECK" => CheckCommand.Run(console),
                "PHOTOS" => PhotosCommand.RunAsync(console, new PhotosOptions(
                    Get(options, "out", "photos"),
                    Int(options, "count", 1500),
                    Get(options, "units", string.Join(',', SmithsonianPhotos.DefaultUnits)).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    ULong(options, "seed", 1)), CancellationToken.None).GetAwaiter().GetResult(),
                "GENERATE" => Generate(console, options),
                "TRAIN" => TrainCommand.Run(console, new TrainOptions(
                    Get(options, "data", "albums"),
                    Get(options, "out", "models"),
                    OptionalInt(options, "epochs"),
                    OptionalInt(options, "batch"),
                    OptionalInt(options, "size"),
                    options.ContainsKey("cpu"))),
                _ => Help(console),
            };
        }
        catch (FormatException ex)
        {
            console.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return 2;
        }
    }

    private static int Generate(IAnsiConsole console, Dictionary<string, string> options)
    {
        var generate = new GenerateOptions(
            Get(options, "out", "albums"),
            Int(options, "count", 8000),
            Int(options, "size", 512),
            ULong(options, "seed", 1),
            Get(options, "photos", string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return options.TryGetValue("preview", out string? sheet)
            ? GenerateCommand.Preview(console, generate with { Count = Math.Min(generate.Count, 16) }, sheet.Length == 0 ? "preview.jpg" : sheet)
            : GenerateCommand.Run(console, generate);
    }

    private static int Help(IAnsiConsole console)
    {
        console.Write(new Text(
            """
            PhotoCropper trainer: makes practice album pages and trains the photo-detection model on them.

              check                        What this computer has, GPU or CPU, and how long training would take.
              photos   [--out photos] [--count 1500] [--units nmaahc,npg,nmah,aaa]
                                           Downloads public-domain (CC0) photographs from Smithsonian Open Access.
              generate [--out albums] [--count 8000] [--size 512] [--photos <folder>,<folder>] [--seed 1]
                                           Makes album pages with their photo masks and boxes. Run it again to carry on.
              generate --preview [sheet.jpg] [--photos <folder>] [--count 16]
                                           Draws a few pages with their masks outlined, to see what the set looks like.
              train    [--data albums] [--out models] [--epochs N] [--batch N] [--size N] [--cpu]
                                           Trains the network (GPU when it can). Stop any time; run again to carry on.

            """));
        return 0;
    }

    private static Dictionary<string, string> Options(IEnumerable<string> args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? name = null;
        foreach (string arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                name = arg[2..];
                options[name] = string.Empty;
            }
            else if (name != null)
            {
                options[name] = arg;
                name = null;
            }
            else
            {
                throw new FormatException($"Unexpected '{arg}': options are written as --name value.");
            }
        }
        return options;
    }

    private static string Get(Dictionary<string, string> options, string name, string fallback) =>
        options.TryGetValue(name, out string? value) && value.Length > 0 ? value : fallback;

    private static int Int(Dictionary<string, string> options, string name, int fallback) =>
        options.TryGetValue(name, out string? value) && value.Length > 0
            ? int.TryParse(value, CultureInfo.InvariantCulture, out int number) && number > 0 ? number : throw new FormatException($"--{name} expects a positive whole number, not '{value}'.")
            : fallback;

    private static int? OptionalInt(Dictionary<string, string> options, string name) =>
        options.ContainsKey(name) ? Int(options, name, 0) : null;

    private static ulong ULong(Dictionary<string, string> options, string name, ulong fallback) =>
        options.TryGetValue(name, out string? value) && value.Length > 0
            ? ulong.TryParse(value, CultureInfo.InvariantCulture, out ulong number) ? number : throw new FormatException($"--{name} expects a whole number, not '{value}'.")
            : fallback;
}
