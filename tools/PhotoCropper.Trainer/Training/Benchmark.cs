using System.Diagnostics;
using TorchSharp;
using static TorchSharp.torch;

namespace PhotoCropper.Trainer.Training;

/// <summary>Times a few real training steps of a plan's network on a device, to estimate how long the plan takes.</summary>
internal static class Benchmark
{
    /// <param name="warmUp">Steps run first and not timed (the first steps load and tune kernels).</param>
    /// <param name="timed">Steps timed.</param>
    /// <returns>Seconds per training step.</returns>
    public static double SecondsPerStep(TrainingPlan plan, Device device, int warmUp, int timed)
    {
        ArgumentNullException.ThrowIfNull(plan);
        using var model = new UNet(plan.Width);
        using var step = new TrainingStep(model, device);
        using var scope = NewDisposeScope();
        // Random pages and masks: the time of a step doesn't depend on what the pages show.
        var images = rand([plan.BatchSize, 3, plan.ImageSize, plan.ImageSize], device: device);
        var masks = (rand([plan.BatchSize, 1, plan.ImageSize, plan.ImageSize], device: device) > 0.7).to_type(ScalarType.Float32);

        for (int i = 0; i < warmUp; i++) step.Run(images, masks);
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < timed; i++) step.Run(images, masks);
        return watch.Elapsed.TotalSeconds / timed;
    }
}
