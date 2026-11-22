using System.Text.Json;
using Microsoft.ML.OnnxRuntime.Tensors;
using PhotoCropper.Trainer.Export;
using PhotoCropper.Trainer.Training;
using Spectre.Console;
using TorchSharp;
using static TorchSharp.torch;

namespace PhotoCropper.Trainer.Commands;

/// <summary>
/// Turns a trained network (best.dat) into an ONNX model for the app, then runs both on the same random page and
/// compares their outputs, so a mistake in the export can't go unnoticed.
/// </summary>
internal static class ExportCommand
{
    public static int Run(IAnsiConsole console, string modelPath, string output)
    {
        if (!File.Exists(modelPath))
        {
            console.MarkupLine($"[red]No model at {Markup.Escape(modelPath)}.[/]");
            return 1;
        }
        string statePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(modelPath))!, "state.json");
        int width = File.Exists(statePath) ? JsonSerializer.Deserialize<TrainingState>(File.ReadAllText(statePath))!.Width : 16;

        using var model = new UNet(width);
        model.load(modelPath);
        model.eval();
        byte[] onnx = UNetExport.ToOnnx(model);
        File.WriteAllBytes(output, onnx);

        // The same page through both: TorchSharp's network and ONNX Runtime's reading of the file.
        const int size = 256;
        var rng = new Rng(42);
        var pixels = new float[3 * size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (float)rng.NextDouble();

        float[] expected;
        using (var scope = NewDisposeScope())
        using (no_grad())
        {
            var input = tensor(pixels, [1, 3, size, size]);
            expected = model.forward(input).data<float>().ToArray();
        }
        using var runner = new PhotoMaskModel(onnx, size);
        float[] actual = runner.Run(new DenseTensor<float>(pixels, [1, 3, size, size]));

        double maxDifference = expected.Zip(actual, (a, b) => Math.Abs(a - b)).Max();
        int disagreements = expected.Zip(actual, (a, b) => (a > 0) != (b > 0)).Count(d => d);
        console.MarkupLine($"Wrote {Markup.Escape(Path.GetFullPath(output))} ({onnx.Length / 1024.0 / 1024.0:F1} MB).");
        console.MarkupLine($"Check against TorchSharp: largest difference {maxDifference:G3}; pixels classified differently: {disagreements} of {expected.Length:N0}.");
        if (maxDifference > 1e-2)
        {
            console.MarkupLine("[red]The exported model doesn't match the trained one.[/]");
            return 1;
        }
        console.MarkupLine("[green]The exported model matches the trained one.[/]");
        return 0;
    }
}
