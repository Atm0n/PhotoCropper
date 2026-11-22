using System.Diagnostics;
using System.Text.Json;
using PhotoCropper.Trainer.Platform;
using PhotoCropper.Trainer.Training;
using Spectre.Console;
using TorchSharp;
using static TorchSharp.torch;

namespace PhotoCropper.Trainer.Commands;

/// <summary>Options for <c>train</c>. Null sizes and counts take the plan's (GPU or CPU) values.</summary>
internal sealed record TrainOptions(string Data, string Output, int? Epochs, int? BatchSize, int? Size, bool ForceCpu);

/// <summary>Where a training run is, saved after every pass so a stopped run carries on.</summary>
internal sealed record TrainingState(int EpochsDone, int Epochs, int Size, int Width, int BatchSize, double BestIou, int BestEpoch);

/// <summary>
/// Trains the network on the generated pages: passes over the training pages, each followed by a check on the pages held
/// back. Keeps the best network so far (models/best.dat) and the latest (models/last.dat), so a run that is stopped can
/// be started again and carries on after the last finished pass.
/// </summary>
internal static class TrainCommand
{
    private const double StartLearningRate = 1e-3, EndLearningRate = 1e-5;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static int Run(IAnsiConsole console, TrainOptions options)
    {
        var platform = PlatformInfo.Detect();
        var (device, plan, reason) = options.ForceCpu ? (CPU, TrainingPlan.Cpu, "--cpu given.") : DeviceChoice.Choose(platform);
        console.MarkupLine(device.type == DeviceType.CUDA
            ? $"Training on the [green]GPU[/] ({Markup.Escape(platform.Nvidia!.Name)})."
            : $"Training on the [yellow]CPU[/]. {Markup.Escape(reason ?? string.Empty)}");

        var dataset = AlbumDataset.Open(options.Data);
        if (dataset.Training.Count == 0)
        {
            console.MarkupLine($"[red]No pages in {Markup.Escape(options.Data)}: run generate first.[/]");
            return 1;
        }

        Directory.CreateDirectory(options.Output);
        string statePath = Path.Combine(options.Output, "state.json");
        string lastPath = Path.Combine(options.Output, "last.dat"), bestPath = Path.Combine(options.Output, "best.dat");
        var saved = File.Exists(statePath) && File.Exists(lastPath) ? JsonSerializer.Deserialize<TrainingState>(File.ReadAllText(statePath)) : null;

        // A run carries on with its own settings; a new run takes the options, or the plan for this device.
        var state = saved ?? new TrainingState(
            EpochsDone: 0,
            Epochs: options.Epochs ?? plan.Epochs,
            Size: options.Size ?? plan.ImageSize,
            Width: plan.Width,
            BatchSize: options.BatchSize ?? plan.BatchSize,
            BestIou: 0,
            BestEpoch: 0);
        if (saved != null && options.Epochs is { } more && more > saved.Epochs) state = state with { Epochs = more };
        if (state.Size % 16 != 0)
        {
            console.MarkupLine("[red]--size must be a multiple of 16.[/]");
            return 2;
        }

        using var model = new UNet(state.Width);
        if (saved != null)
        {
            model.load(lastPath);
            console.MarkupLine($"Carrying on after pass {saved.EpochsDone} of {state.Epochs} (best so far: {saved.BestIou:P1} at pass {saved.BestEpoch}).");
        }
        if (state.EpochsDone >= state.Epochs)
        {
            console.MarkupLine($"[green]Already trained:[/] {state.Epochs} passes. The model is {Markup.Escape(Path.GetFullPath(bestPath))}. Give a larger --epochs to train more.");
            return 0;
        }

        console.MarkupLine($"{dataset.Training.Count:N0} pages to train on, {dataset.Validation.Count:N0} held back to check on; pages of {state.Size} px, {state.BatchSize} per step, {state.Epochs} passes.");
        console.MarkupLine($"[grey]Network: {model.WeightCount():N0} weights. Stop with Ctrl+C at any time: the next run carries on after the last finished pass.[/]");

        using var step = new TrainingStep(model, device);
        int stepsPerEpoch = (int)Math.Ceiling(dataset.Training.Count / (double)state.BatchSize);
        long totalSteps = (long)stepsPerEpoch * state.Epochs;
        var results = new Table().Border(TableBorder.Rounded)
            .AddColumns("Pass", "Training loss", "Check loss", "Photo overlap (IoU)", "Time");
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            // Finish the step, save, then stop.
            e.Cancel = true;
            stop.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            for (int epoch = state.EpochsDone + 1; epoch <= state.Epochs && !stop.IsCancellationRequested; epoch++)
            {
                var watch = Stopwatch.StartNew();
                var rng = new Rng((ulong)epoch * 7919UL);
                double lossSum = 0;
                int steps = 0;

                console.Progress().AutoClear(true).Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new RemainingTimeColumn())
                    .Start(context =>
                    {
                        var task = context.AddTask($"Pass {epoch} of {state.Epochs}", maxValue: stepsPerEpoch);
                        var reader = dataset.Batches(dataset.Training, state.BatchSize, state.Size, augment: true, rng, stop.Token);
                        while (!stop.IsCancellationRequested && reader.WaitToReadAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult())
                        {
                            if (!reader.TryRead(out var batch)) continue;
                            long globalStep = ((long)(epoch - 1) * stepsPerEpoch) + steps;
                            step.LearningRate = LearningRateAt(globalStep, totalSteps);
                            using (var scope = NewDisposeScope())
                            {
                                var (images, masks) = ToTensors(batch, device);
                                lossSum += step.Run(images, masks);
                            }
                            steps++;
                            task.Description = $"Pass {epoch} of {state.Epochs}: loss {lossSum / steps:F3}";
                            task.Increment(1);
                        }
                    });

                if (stop.IsCancellationRequested)
                {
                    console.MarkupLine($"[yellow]Stopped during pass {epoch}.[/] Run train again to carry on after pass {state.EpochsDone}.");
                    break;
                }

                var check = Evaluate(dataset, step, state, device);
                bool best = check.Iou > state.BestIou;
                state = state with
                {
                    EpochsDone = epoch,
                    BestIou = best ? check.Iou : state.BestIou,
                    BestEpoch = best ? epoch : state.BestEpoch,
                };
                model.save(lastPath);
                if (best) model.save(bestPath);
                File.WriteAllText(statePath, JsonSerializer.Serialize(state, Json));

                results.AddRow($"{epoch}{(best ? " ★" : string.Empty)}", $"{lossSum / Math.Max(1, steps):F3}", $"{check.Loss:F3}", $"{check.Iou:P1}", CheckCommand.Duration(watch.Elapsed.TotalSeconds));
                console.MarkupLine($"Pass {epoch}: training loss {lossSum / Math.Max(1, steps):F3}, check loss {check.Loss:F3}, photo overlap [bold]{check.Iou:P1}[/]{(best ? " [green](best so far, saved)[/]" : string.Empty)} in {CheckCommand.Duration(watch.Elapsed.TotalSeconds)}.");
            }
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }

        console.Write(results);
        if (state.EpochsDone >= state.Epochs)
        {
            console.MarkupLine($"[green]Training finished.[/] Best: {state.BestIou:P1} photo overlap at pass {state.BestEpoch}: {Markup.Escape(Path.GetFullPath(bestPath))}");
        }
        return 0;
    }

    /// <summary>Cosine schedule: large steps at first, smoothly smaller towards the end.</summary>
    private static double LearningRateAt(long step, long total) =>
        EndLearningRate + (0.5 * (StartLearningRate - EndLearningRate) * (1 + Math.Cos(Math.PI * Math.Min(1.0, step / (double)Math.Max(1, total)))));

    private static (Tensor Images, Tensor Masks) ToTensors(Batch batch, Device device) =>
        (tensor(batch.Images, [batch.Count, 3, batch.Size, batch.Size], device: device),
         tensor(batch.Masks, [batch.Count, 1, batch.Size, batch.Size], device: device));

    private static Evaluation Evaluate(AlbumDataset dataset, TrainingStep step, TrainingState state, Device device)
    {
        double loss = 0, intersection = 0, union = 0;
        int batches = 0;
        var reader = dataset.Batches(dataset.Validation, state.BatchSize, state.Size, augment: false, new Rng(1), CancellationToken.None);
        while (reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
        {
            if (!reader.TryRead(out var batch)) continue;
            using var scope = NewDisposeScope();
            var (images, masks) = ToTensors(batch, device);
            var (l, i, u) = step.Evaluate(images, masks);
            loss += l;
            intersection += i;
            union += u;
            batches++;
        }
        return new Evaluation(loss / Math.Max(1, batches), union > 0 ? intersection / union : 0);
    }
}
