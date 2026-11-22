using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PhotoCropper.Trainer.Export;

/// <summary>Runs an exported model's raw output through ONNX Runtime, to check the export against TorchSharp.</summary>
internal sealed class PhotoMaskModel(byte[] model) : IDisposable
{
    private readonly InferenceSession _session = new(model);

    /// <summary>The network's raw output (logits) for an input tensor (N × 3 × H × W).</summary>
    public float[] Run(DenseTensor<float> input)
    {
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(UNetExport.InputName, input)]);
        return [.. results[0].AsEnumerable<float>()];
    }

    public void Dispose() => _session.Dispose();
}
