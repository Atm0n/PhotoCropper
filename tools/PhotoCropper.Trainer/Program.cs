using PhotoCropper.Trainer;
using PhotoCropper.Trainer.Commands;
using Spectre.Console;

// Box-drawing characters and accents in titles need UTF-8 on Windows consoles.
Console.OutputEncoding = System.Text.Encoding.UTF8;
return CommandLine.Run(args, AnsiConsole.Console);
