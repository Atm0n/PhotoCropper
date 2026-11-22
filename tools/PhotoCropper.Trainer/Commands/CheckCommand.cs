using PhotoCropper.Trainer.Album;
using PhotoCropper.Trainer.Platform;
using PhotoCropper.Trainer.Training;
using Spectre.Console;
using System.Runtime.InteropServices;
using TorchSharp;
using static TorchSharp.torch;

namespace PhotoCropper.Trainer.Commands;

/// <summary>
/// Looks at this computer, says whether training will use the GPU or the CPU, and estimates how long making the
/// album pages and training will take, by timing a few of each.
/// </summary>
internal static class CheckCommand
{
    public static int Run(IAnsiConsole console)
    {
        var platform = PlatformInfo.Detect();
        ShowPlatform(console, platform);

        var (device, plan, reason) = DeviceChoice.Choose(platform);
        console.WriteLine();
        console.MarkupLine(device.type == DeviceType.CUDA
            ? $"[green]Training will run on the GPU[/] ({Markup.Escape(platform.Nvidia!.Name)})."
            : "[yellow]Training will run on the CPU.[/]");
        if (reason != null) console.MarkupLine($"  [grey]{Markup.Escape(reason)}[/]");

        console.WriteLine();
        double secondsPerPage = 0, secondsPerStep = 0;
        string? benchmarkFailure = null;
        console.Status().Start("Timing album page generation...", _ =>
        {
            secondsPerPage = AlbumTiming.SecondsPerPage(plan.ImageSize);
        });
        console.Status().Start($"Timing training steps on the {(device.type == DeviceType.CUDA ? "GPU" : "CPU")}...", _ =>
        {
            try
            {
                bool gpu = device.type == DeviceType.CUDA;
                secondsPerStep = Benchmark.SecondsPerStep(plan, device, warmUp: gpu ? 5 : 1, timed: gpu ? 20 : 3);
            }
            catch (ExternalException ex)
            {
                benchmarkFailure = ex.Message;
            }
        });

        if (benchmarkFailure != null && device.type == DeviceType.CUDA)
        {
            // The card is there but this build of the native library can't run on it (e.g. no kernels for it).
            console.MarkupLine($"[red]The GPU could not run a training step:[/] {Markup.Escape(FirstLine(benchmarkFailure))}");
            console.MarkupLine("  [grey]Falling back to the CPU. If the driver is recent, this LibTorch build may not support the card yet: see README.md (Python fallback).[/]");
            device = CPU;
            plan = TrainingPlan.Cpu;
            secondsPerStep = Benchmark.SecondsPerStep(plan, device, warmUp: 1, timed: 3);
        }

        ShowEstimate(console, plan, secondsPerPage, secondsPerStep);
        if (device.type != DeviceType.CUDA)
        {
            // What the full plan would cost here, for comparison.
            double fullStep = Benchmark.SecondsPerStep(TrainingPlan.Gpu with { Name = "Full plan" }, device, warmUp: 1, timed: 2);
            console.MarkupLine($"  [grey]The full plan ({TrainingPlan.Gpu.Pages:N0} pages of {TrainingPlan.Gpu.ImageSize} px) would take about {Duration(fullStep * TrainingPlan.Gpu.Steps)} to train on this CPU.[/]");
        }
        return 0;
    }

    private static void ShowPlatform(IAnsiConsole console, PlatformInfo platform)
    {
        var table = new Table().Border(TableBorder.Rounded).AddColumn("This computer").AddColumn(string.Empty);
        table.AddRow("System", Markup.Escape($"{platform.OperatingSystem} ({platform.Architecture})"));
        table.AddRow("Processor", Markup.Escape($"{platform.CpuName}, {platform.LogicalCores} threads"));
        table.AddRow("Memory", $"{platform.MemoryBytes / (1024.0 * 1024 * 1024):F1} GB");
        table.AddRow("Graphics", platform.GraphicsAdapters.Count == 0 ? "-" : Markup.Escape(string.Join(", ", platform.GraphicsAdapters)));
        table.AddRow("NVIDIA", platform.Nvidia is { } gpu
            ? Markup.Escape($"{gpu.Name}, {gpu.MemoryMiB / 1024.0:F0} GB, driver {gpu.DriverVersion}")
            : "none");
        table.AddRow("Training library", Markup.Escape($"TorchSharp ({DeviceChoice.BuildBackend} build)"));
        console.Write(table);
    }

    private static void ShowEstimate(IAnsiConsole console, TrainingPlan plan, double secondsPerPage, double secondsPerStep)
    {
        double generate = secondsPerPage * plan.Pages;
        double train = secondsPerStep * plan.Steps;
        var table = new Table().Border(TableBorder.Rounded)
            .AddColumn(Markup.Escape(plan.Name)).AddColumn(new TableColumn("Estimated time").RightAligned());
        table.AddRow(Markup.Escape($"Make {plan.Pages:N0} album pages of {plan.ImageSize} px"), Duration(generate));
        table.AddRow(Markup.Escape($"Train: {plan.Epochs} passes, {plan.Steps:N0} steps of {plan.BatchSize} pages ({secondsPerStep:F2} s each)"), Duration(train));
        table.AddRow("[bold]Total[/]", $"[bold]{Duration(generate + train)}[/]");
        console.Write(table);
        console.MarkupLine("  [grey]Estimates from timing a few pages and steps; the real run can differ by a fair margin.[/]");
    }

    internal static string Duration(double seconds) => seconds switch
    {
        < 90 => $"{seconds:F0} s",
        < 5400 => $"{seconds / 60:F0} min",
        < 172_800 => $"{seconds / 3600:F1} h",
        _ => $"{seconds / 86_400:F1} days",
    };

    private static string FirstLine(string text) => text.Split('\n')[0].Trim();
}
