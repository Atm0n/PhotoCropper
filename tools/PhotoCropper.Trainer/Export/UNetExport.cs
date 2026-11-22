using PhotoCropper.Trainer.Training;
using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;

namespace PhotoCropper.Trainer.Export;

/// <summary>
/// Turns a trained <see cref="UNet"/> into an ONNX model. Batch normalisation is folded into the convolution before
/// it (as it behaves after training, it is just a scale and shift per channel), so the model is only convolutions,
/// ReLU, max pooling, transposed convolutions and concatenation.
/// </summary>
internal static class UNetExport
{
    public const string InputName = "image", OutputName = "logits";

    public static byte[] ToOnnx(UNet model)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.eval();
        var graph = new OnnxGraph();
        var parts = model.Parts();

        string e1 = Block(graph, "enc1", parts.Enc1, InputName);
        string e2 = Block(graph, "enc2", parts.Enc2, Pool(graph, e1));
        string e3 = Block(graph, "enc3", parts.Enc3, Pool(graph, e2));
        string e4 = Block(graph, "enc4", parts.Enc4, Pool(graph, e3));
        string b = Block(graph, "bottleneck", parts.Bottleneck, Pool(graph, e4));
        string d4 = Block(graph, "dec4", parts.Dec4, Concat(graph, Up(graph, "up4", parts.Up4, b), e4));
        string d3 = Block(graph, "dec3", parts.Dec3, Concat(graph, Up(graph, "up3", parts.Up3, d4), e3));
        string d2 = Block(graph, "dec2", parts.Dec2, Concat(graph, Up(graph, "up2", parts.Up2, d3), e2));
        string d1 = Block(graph, "dec1", parts.Dec1, Concat(graph, Up(graph, "up1", parts.Up1, d2), e1));
        Conv(graph, "head", Values(parts.Head.weight!), Values(parts.Head.bias!), parts.Head.weight!.shape, d1, padding: 0, output: OutputName);

        return graph.Model(InputName, OutputName, "PhotoCropper.Trainer");
    }

    /// <summary>Conv → BatchNorm → ReLU, twice (see <see cref="UNet"/>).</summary>
    private static string Block(OnnxGraph graph, string name, Sequential block, string input)
    {
        var layers = block.children().ToList();
        string x = input;
        for (int i = 0; i < 2; i++)
        {
            var conv = (Conv2d)layers[i * 3];
            var norm = (BatchNorm2d)layers[(i * 3) + 1];
            var (weight, bias) = Fold(conv, norm);
            x = Conv(graph, $"{name}_{i}", weight, bias, conv.weight!.shape, x, padding: 1);
            x = graph.Node("Relu", [x]);
        }
        return x;
    }

    /// <summary>The convolution's weights with the batch normalisation after it folded in: w·γ/σ and β − μ·γ/σ.</summary>
    private static (float[] Weight, float[] Bias) Fold(Conv2d conv, BatchNorm2d norm)
    {
        float[] w = Values(conv.weight!);
        float[] gamma = Values(norm.weight!), beta = Values(norm.bias!), mean = Values(norm.running_mean!), variance = Values(norm.running_var!);
        int outputs = gamma.Length, perOutput = w.Length / outputs;
        var bias = new float[outputs];
        for (int o = 0; o < outputs; o++)
        {
            float scale = gamma[o] / MathF.Sqrt(variance[o] + 1e-5f);
            for (int k = 0; k < perOutput; k++) w[(o * perOutput) + k] *= scale;
            bias[o] = beta[o] - (mean[o] * scale);
        }
        return (w, bias);
    }

    private static string Conv(OnnxGraph graph, string name, float[] weight, float[] bias, long[] shape, string input, int padding, string? output = null)
    {
        string w = graph.Weight($"{name}.weight", weight, shape);
        string b = graph.Weight($"{name}.bias", bias, shape[0]);
        return graph.Node("Conv", [input, w, b], new Dictionary<string, long[]>
        {
            ["kernel_shape"] = [shape[2], shape[3]],
            ["pads"] = [padding, padding, padding, padding],
            ["strides"] = [1, 1],
        }, output: output);
    }

    private static string Pool(OnnxGraph graph, string input) => graph.Node("MaxPool", [input], new Dictionary<string, long[]>
    {
        ["kernel_shape"] = [2, 2],
        ["strides"] = [2, 2],
    });

    private static string Up(OnnxGraph graph, string name, ConvTranspose2d up, string input)
    {
        string w = graph.Weight($"{name}.weight", Values(up.weight!), up.weight!.shape);
        string b = graph.Weight($"{name}.bias", Values(up.bias!), up.bias!.shape);
        return graph.Node("ConvTranspose", [input, w, b], new Dictionary<string, long[]>
        {
            ["kernel_shape"] = [2, 2],
            ["strides"] = [2, 2],
        });
    }

    /// <summary>The upsampled map first, then the skip link: the order the network concatenates them in.</summary>
    private static string Concat(OnnxGraph graph, string upsampled, string skip) =>
        graph.Node("Concat", [upsampled, skip], intAttributes: new Dictionary<string, long> { ["axis"] = 1 });

    private static float[] Values(Tensor tensor)
    {
        using var detached = tensor.detach();
        using var onCpu = detached.cpu();
        return onCpu.data<float>().ToArray();
    }
}
