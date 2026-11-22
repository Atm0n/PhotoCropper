using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Extraction;
using PhotoCropper.Core.Models;

namespace PhotoCropper.Core.Detection;

/// <summary>
/// Finds the photos on a scan with a trained network (tools/PhotoCropper.Trainer): it marks which pixels are photo,
/// and each separate area becomes a rotated box around the whole print. The scan is prepared exactly as the practice
/// pages were: its long side scaled to the model's page size, centred on black, BGR values 0..1.
/// </summary>
/// <remarks>
/// The model is <see cref="ModelFileName"/>, looked for in the per-user model folder (a newly trained one dropped
/// there wins) and then next to the app. Without one, AI detection isn't available and detection is unchanged.
/// </remarks>
public static class AiPhotoDetector
{
    public const string ModelFileName = "photo-mask.onnx";

    /// <summary>The gap the training masks keep between neighbouring photos, in model pixels (half on each).</summary>
    public const float MaskGap = 1.5f;

    /// <summary>The page size when the model doesn't say (its "input_size" metadata).</summary>
    public const int DefaultInputSize = 512;

    /// <summary>A shape smaller than this share of the page is noise, not a photo.</summary>
    private const double MinAreaFraction = 0.003;

    private static readonly Lock LoadLock = new();
    private static InferenceSession? _session;
    private static string? _loadedFrom;
    private static int _inputSize = DefaultInputSize;

    /// <summary>A model to use instead of the installed one (tests, the trainer's score command). Null for the installed one.</summary>
    public static string? ModelOverride { get; set; }

    /// <summary>The model file in use, or null when none is installed.</summary>
    public static string? ModelPath =>
        ModelOverride ?? new[]
        {
            Path.Combine(AiModelStore.ModelDirectory, ModelFileName),
            Path.Combine(AppContext.BaseDirectory, "models", ModelFileName),
        }.FirstOrDefault(File.Exists);

    /// <summary>True when a model is installed, so AI detection can be used.</summary>
    public static bool IsAvailable => ModelPath != null;

    /// <summary>The photos on <paramref name="scan"/> (8-bit BGR), in its pixels; empty when no model is installed.</summary>
    public static IReadOnlyList<RotatedBox> FindPhotos(Mat scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        if (scan.Empty() || Session() is not { } session) return [];
        int size;
        lock (LoadLock) size = _inputSize;

        double scale = (double)size / Math.Max(scan.Width, scan.Height);
        int w = Math.Max(1, (int)Math.Round(scan.Width * scale)), h = Math.Max(1, (int)Math.Round(scan.Height * scale));
        int offsetX = (size - w) / 2, offsetY = (size - h) / 2;

        var input = new DenseTensor<float>([1, 3, size, size]);
        using (Mat small = new())
        using (Mat square = new(size, size, MatType.CV_8UC3, Scalar.All(0)))
        {
            Cv2.Resize(scan, small, new Size(w, h), 0, 0, InterpolationFlags.Area);
            using (Mat inside = square[new Rect(offsetX, offsetY, w, h)]) small.CopyTo(inside);
            var bgr = new byte[size * size * 3];
            Marshal.Copy(square.Data, bgr, 0, bgr.Length);
            int pixels = size * size;
            var buffer = input.Buffer.Span;
            for (int p = 0; p < pixels; p++)
            {
                for (int c = 0; c < 3; c++) buffer[(c * pixels) + p] = bgr[(p * 3) + c] / 255f;
            }
        }

        var maskBytes = new byte[size * size];
        using (var results = session.Run([NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), input)]))
        {
            int p = 0;
            foreach (float logit in results[0].AsEnumerable<float>())
            {
                if (p >= maskBytes.Length) break;
                maskBytes[p++] = logit > 0 ? (byte)255 : (byte)0;
            }
        }

        using Mat full = Mat.FromPixelData(size, size, MatType.CV_8UC1, maskBytes);
        using Mat onScan = full[new Rect(offsetX, offsetY, w, h)];
        using Mat mask = onScan.Clone();
        Cv2.FindContours(mask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var boxes = new List<RotatedBox>();
        double minArea = MinAreaFraction * w * h;
        // The box runs through the centres of the edge pixels: add one pixel for their width, and the gap the mask
        // keeps between neighbouring photos; then return to scan pixels.
        float grow = 1 + (2 * MaskGap);
        foreach (var contour in contours)
        {
            if (Cv2.ContourArea(contour) < minArea) continue;
            var box = Cv2.MinAreaRect(contour);
            var inScan = new RotatedRect(
                new Point2f((float)((box.Center.X + 0.5) / scale), (float)((box.Center.Y + 0.5) / scale)),
                new Size2f((float)((box.Size.Width + grow) / scale), (float)((box.Size.Height + grow) / scale)),
                box.Angle);
            boxes.Add(CvInterop.ToBox(inScan));
        }
        return boxes;
    }

    /// <summary>The loaded model, loading it (again) when the file to use has changed; null when there is none.</summary>
    private static InferenceSession? Session()
    {
        string? path = ModelPath;
        lock (LoadLock)
        {
            if (path == null)
            {
                _session?.Dispose();
                _session = null;
                _loadedFrom = null;
                return null;
            }
            if (_session != null && _loadedFrom == path) return _session;

            _session?.Dispose();
            _session = new InferenceSession(path);
            _loadedFrom = path;
            _inputSize = _session.ModelMetadata.CustomMetadataMap.TryGetValue("input_size", out string? text)
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0 && value % 16 == 0
                ? value
                : DefaultInputSize;
            return _session;
        }
    }
}
