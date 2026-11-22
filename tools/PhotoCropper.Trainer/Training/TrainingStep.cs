using TorchSharp;
using static TorchSharp.torch;

namespace PhotoCropper.Trainer.Training;

/// <summary>
/// One step of training: the network guesses masks for a batch of pages, the loss measures how wrong they are, and the
/// optimiser nudges every weight a little towards a better guess.
/// </summary>
internal sealed class TrainingStep : IDisposable
{
    private readonly UNet _model;
    private readonly optim.Optimizer _optimizer;

    public TrainingStep(UNet model, Device device, double learningRate = 1e-3)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        Device = device;
        _model.to(device);
        _model.train();
        _optimizer = optim.AdamW(_model.parameters(), lr: learningRate, weight_decay: 1e-4);
    }

    public Device Device { get; }

    /// <param name="images">A batch of pages: N × 3 × S × S, values 0..1, on <see cref="Device"/>.</param>
    /// <param name="masks">Their masks: N × 1 × S × S, 1 on photo pixels and 0 elsewhere.</param>
    /// <returns>The batch's loss before the step (lower is better).</returns>
    public float Run(Tensor images, Tensor masks)
    {
        using var scope = NewDisposeScope();
        _optimizer.zero_grad();
        var logits = _model.forward(images);
        var loss = Loss(logits, masks);
        loss.backward();
        _optimizer.step();
        // Reading the value also waits for the GPU to finish the step, so timing a step is honest.
        return loss.item<float>();
    }

    /// <summary>
    /// Binary cross-entropy (each pixel right or wrong) plus Dice loss (how well the photo areas overlap as shapes, so
    /// small photos count as much as the large page around them).
    /// </summary>
    public static Tensor Loss(Tensor logits, Tensor masks)
    {
        ArgumentNullException.ThrowIfNull(logits);
        ArgumentNullException.ThrowIfNull(masks);
        using var scope = NewDisposeScope();
        var bce = nn.functional.binary_cross_entropy_with_logits(logits, masks);
        var probabilities = logits.sigmoid();
        var overlap = (probabilities * masks).sum([1, 2, 3]);
        var total = probabilities.sum([1, 2, 3]) + masks.sum([1, 2, 3]);
        var dice = 1 - ((2 * overlap + 1) / (total + 1)).mean();
        return (bce + dice).MoveToOuterDisposeScope();
    }

    public void Dispose() => _optimizer.Dispose();
}
