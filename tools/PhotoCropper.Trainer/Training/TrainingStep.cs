using TorchSharp;
using static TorchSharp.torch;

namespace PhotoCropper.Trainer.Training;

/// <summary>How the network does on pages it isn't trained on.</summary>
/// <param name="Loss">The same loss as training (lower is better).</param>
/// <param name="Iou">Photo pixels found ∩ true photo pixels, over their union: 1 is perfect.</param>
internal sealed record Evaluation(double Loss, double Iou);

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

    /// <summary>How big the nudges are. Training starts with larger ones and ends with small ones.</summary>
    public double LearningRate
    {
        set
        {
            foreach (var group in _optimizer.ParamGroups) group.LearningRate = value;
        }
    }

    /// <param name="images">A batch of pages: N × 3 × S × S, values 0..1, on <see cref="Device"/>.</param>
    /// <param name="masks">Their masks: N × 1 × S × S, 1 on photo pixels and 0 elsewhere.</param>
    /// <returns>The batch's loss before the step (lower is better).</returns>
    public float Run(Tensor images, Tensor masks)
    {
        using var scope = NewDisposeScope();
        _model.train();
        _optimizer.zero_grad();
        var logits = _model.forward(images);
        var loss = Loss(logits, masks);
        loss.backward();
        _optimizer.step();
        // Reading the value also waits for the GPU to finish the step, so timing a step is honest.
        return loss.item<float>();
    }

    /// <summary>The loss and the overlap of found and true photo pixels on a batch, without training on it.</summary>
    /// <returns>The batch's loss, and the photo pixels in both the guess and the truth, and in either.</returns>
    public (double Loss, double Intersection, double Union) Evaluate(Tensor images, Tensor masks)
    {
        using var scope = NewDisposeScope();
        using var noGrad = no_grad();
        _model.eval();
        var logits = _model.forward(images);
        var loss = Loss(logits, masks).item<float>();
        var found = (logits > 0).to_type(ScalarType.Float32);
        double intersection = (found * masks).sum().item<float>();
        double union = (found + masks).clamp(0, 1).sum().item<float>();
        return (loss, intersection, union);
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
