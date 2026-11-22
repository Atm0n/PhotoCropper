namespace PhotoCropper.Trainer.Training;

/// <summary>How much training to do: the pages, their size and how many passes over them.</summary>
/// <param name="Name">Shown to the user ("GPU plan", "CPU plan").</param>
/// <param name="Pages">Generated album pages to train on.</param>
/// <param name="ImageSize">Pages are squares of this many pixels (a multiple of 16).</param>
/// <param name="Epochs">Passes over all the pages.</param>
/// <param name="BatchSize">Pages per training step.</param>
/// <param name="Width">The network's width (see <see cref="UNet"/>).</param>
internal sealed record TrainingPlan(string Name, int Pages, int ImageSize, int Epochs, int BatchSize, int Width)
{
    /// <summary>The full plan, for an NVIDIA GPU: about an hour or two on an RTX 5060.</summary>
    public static TrainingPlan Gpu { get; } = new("GPU plan", Pages: 8000, ImageSize: 512, Epochs: 30, BatchSize: 8, Width: 16);

    /// <summary>A lighter plan for a CPU: smaller pages and fewer of them. Expect a weaker model.</summary>
    public static TrainingPlan Cpu { get; } = new("CPU plan", Pages: 3000, ImageSize: 256, Epochs: 20, BatchSize: 8, Width: 16);

    public long Steps => (long)Math.Ceiling((double)Pages / BatchSize) * Epochs;
}
