using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using PhotoCropper.Trainer.Album;

namespace PhotoCropper.Trainer.Export;

/// <summary>
/// Runs an exported photo network (ONNX Runtime, CPU) on a scan and turns its mask into rotated photo boxes, the
/// way the app will. The scan is prepared as the practice pages were: its long side scaled to <see cref="Size"/>,
/// centred on a black square, BGR values 0..1.
/// </summary>
internal sealed class PhotoMaskModel : IDisposable
{
    /// <summary>A shape smaller than this share of the page is noise, not a photo.</summary>
    private const double MinAreaFraction = 0.003;

    private readonly InferenceSession _session;

    public PhotoMaskModel(string path, int size = 512)
    {
        _session = new InferenceSession(path);
        Size = size;
    }

    public PhotoMaskModel(byte[] model, int size = 512)
    {
        _session = new InferenceSession(model);
        Size = size;
    }

    public int Size { get; }

    /// <summary>The network's raw output for an input tensor (N × 3 × H × W).</summary>
    public float[] Run(DenseTensor<float> input)
    {
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(UNetExport.InputName, input)]);
        return [.. results[0].AsEnumerable<float>()];
    }

    /// <summary>The photos on <paramref name="scan"/> (8-bit BGR), as rotated boxes in its pixels.</summary>
    public List<RotatedRect> FindPhotos(Mat scan, out Mat mask)
    {
        ArgumentNullException.ThrowIfNull(scan);
        double scale = (double)Size / Math.Max(scan.Width, scan.Height);
        int w = Math.Max(1, (int)Math.Round(scan.Width * scale)), h = Math.Max(1, (int)Math.Round(scan.Height * scale));
        int offsetX = (Size - w) / 2, offsetY = (Size - h) / 2;

        using Mat small = new();
        Cv2.Resize(scan, small, new OpenCvSharp.Size(w, h), 0, 0, InterpolationFlags.Area);
        using Mat square = new(Size, Size, MatType.CV_8UC3, Scalar.All(0));
        using (Mat inside = square[new Rect(offsetX, offsetY, w, h)]) small.CopyTo(inside);

        var input = new DenseTensor<float>([1, 3, Size, Size]);
        var bgr = new byte[Size * Size * 3];
        System.Runtime.InteropServices.Marshal.Copy(square.Data, bgr, 0, bgr.Length);
        int pixels = Size * Size;
        var buffer = input.Buffer.Span;
        for (int p = 0; p < pixels; p++)
        {
            for (int c = 0; c < 3; c++) buffer[(c * pixels) + p] = bgr[(p * 3) + c] / 255f;
        }

        float[] logits = Run(input);
        var maskBytes = new byte[pixels];
        for (int p = 0; p < pixels; p++) maskBytes[p] = logits[p] > 0 ? (byte)255 : (byte)0;
        using Mat full = Mat.FromPixelData(Size, Size, MatType.CV_8UC1, maskBytes);
        using Mat onScan = full[new Rect(offsetX, offsetY, w, h)];
        mask = onScan.Clone();

        Cv2.FindContours(mask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var boxes = new List<RotatedRect>();
        double minArea = MinAreaFraction * w * h;
        foreach (var contour in contours)
        {
            if (Cv2.ContourArea(contour) < minArea) continue;
            var box = Cv2.MinAreaRect(contour);
            // The box runs through the centres of the edge pixels: add one pixel for their own width, and the thin
            // gap the mask keeps between neighbouring photos; then return to scan pixels.
            float grow = 1 + (2 * AlbumGenerator.MaskGap);
            boxes.Add(new RotatedRect(
                new Point2f((float)((box.Center.X + 0.5) / scale), (float)((box.Center.Y + 0.5) / scale)),
                new Size2f((float)((box.Size.Width + grow) / scale), (float)((box.Size.Height + grow) / scale)),
                box.Angle));
        }
        return boxes;
    }

    public void Dispose() => _session.Dispose();
}
