using OpenCvSharp;
using PhotoCropper.Core.Common;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Security.Cryptography;

namespace PhotoCropper.Core.Extraction;

/// <summary>An inpainting network with a fixed square input (BGR 8-bit image, 8-bit mask: 255 = fill).</summary>
internal interface IInpaintingModel
{
    int InputSize { get; }

    Mat Inpaint(Mat bgr, Mat mask);
}

/// <summary>LaMa running on ONNX Runtime. Thread-safe: one session can run several fills at once.</summary>
internal sealed class LamaModel : IInpaintingModel, IDisposable
{
    private readonly InferenceSession _session;

    public LamaModel(string modelPath)
    {
        using var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        _session = new InferenceSession(modelPath, options);
    }

    public int InputSize => 512;

    public Mat Inpaint(Mat bgr, Mat mask)
    {
        int size = InputSize;
        int plane = size * size;
        byte[] pixels = new byte[plane * 3];
        bgr.CopyDataTo(pixels);
        byte[] maskBytes = new byte[plane];
        mask.CopyDataTo(maskBytes);

        // Model input: RGB in 0..1, planar (1x3xHxW); mask 0/1 (1x1xHxW).
        float[] image = new float[plane * 3];
        float[] holes = new float[plane];
        for (int i = 0; i < plane; i++)
        {
            image[i] = pixels[i * 3 + 2] / 255f;
            image[plane + i] = pixels[i * 3 + 1] / 255f;
            image[2 * plane + i] = pixels[i * 3] / 255f;
            holes[i] = maskBytes[i] > 0 ? 1f : 0f;
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("image", new DenseTensor<float>(image, [1, 3, size, size])),
            NamedOnnxValue.CreateFromTensor("mask", new DenseTensor<float>(holes, [1, 1, size, size])),
        };
        using var results = _session.Run(inputs);
        var output = results[0].AsTensor<float>();

        // Output: RGB in 0..255, planar.
        byte[] result = new byte[plane * 3];
        int index = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++, index++)
            {
                result[index * 3] = ToByte(output[0, 2, y, x]);
                result[index * 3 + 1] = ToByte(output[0, 1, y, x]);
                result[index * 3 + 2] = ToByte(output[0, 0, y, x]);
            }
        }

        Mat? mat = null;
        try
        {
            mat = new Mat(size, size, MatType.CV_8UC3);
            mat.SetData(result);
            Mat filled = mat;
            mat = null; // the caller owns it now
            return filled;
        }
        finally
        {
            mat?.Dispose();
        }
    }

    private static byte ToByte(float value) => (byte)Math.Clamp(MathF.Round(value), 0, 255);

    public void Dispose() => _session.Dispose();
}

/// <summary>
/// AI fill for the repair brush. The network works on a fixed 512×512 square, so each damaged area is cut out with
/// some surroundings for context, scaled to 512, filled, scaled back, and only the painted pixels are replaced.
/// Nearby areas share one square. Results are cached, so re-rendering a preview after a colour change doesn't run
/// the network again.
/// </summary>
public static class AiInpainting
{
    private static readonly Lock LoadLock = new();
    private static IInpaintingModel? _model;
    private static string? _loadedFrom;

    private static readonly Lock CacheLock = new();
    private static readonly LinkedList<(string Key, Mat Result)> Cache = new();
    private const int CacheCapacity = 24;

    /// <summary>Replaces the network (tests). Null returns to the downloaded LaMa model.</summary>
    internal static IInpaintingModel? ModelOverride { get; set; }

    /// <summary>The last error loading the model, if it could not be loaded.</summary>
    public static string? LoadError { get; private set; }

    /// <summary>True when AI fill can be used (the model is downloaded, or a test model is set).</summary>
    public static bool IsAvailable => ModelOverride != null || AiModelStore.IsLamaInstalled;

    /// <summary>True once the model is loaded in memory (loading takes several seconds).</summary>
    public static bool IsLoaded => ModelOverride != null || _model != null;

    /// <summary>Loads the model in the background so the first fill doesn't wait for it.</summary>
    public static Task PreloadAsync() => Task.Run(() => GetModel());

    private static IInpaintingModel? GetModel()
    {
        if (ModelOverride != null) return ModelOverride;
        if (!AiModelStore.IsLamaInstalled) return null;

        lock (LoadLock)
        {
            string path = AiModelStore.LamaModelPath;
            if (_model != null && _loadedFrom == path) return _model;
            try
            {
                (_model as IDisposable)?.Dispose();
                _model = new LamaModel(path);
                _loadedFrom = path;
                LoadError = null;
                return _model;
            }
            catch (OnnxRuntimeException ex)
            {
                _model = null;
                LoadError = ex.Message;
                return null;
            }
        }
    }

    /// <summary>
    /// Fills the masked areas (255) of a 1-, 3- or 4-channel 8-bit image. Returns a new image, or null when the
    /// model isn't available. Alpha is kept as it was.
    /// </summary>
    public static Mat? Fill(Mat image, Mat mask)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(mask);
        IInpaintingModel? model = GetModel();
        if (model == null) return null;

        try
        {
            return FillWithModel(model, image, mask);
        }
        catch (OnnxRuntimeException ex)
        {
            // A broken model shouldn't break the export or the preview: the caller falls back to a quick fill.
            LoadError = ex.Message;
            return null;
        }
    }

    private static Mat FillWithModel(IInpaintingModel model, Mat image, Mat mask)
    {
        if (image.Channels() == 3) return FillBgr(model, image, mask);

        using Mat bgr = new();
        Cv2.CvtColor(image, bgr, image.Channels() == 4 ? ColorConversionCodes.BGRA2BGR : ColorConversionCodes.GRAY2BGR);
        using Mat filled = FillBgr(model, bgr, mask);
        Mat result = new();
        if (image.Channels() == 1)
        {
            Cv2.CvtColor(filled, result, ColorConversionCodes.BGR2GRAY);
            return result;
        }

        using var channels = MatChannels.Split(image);
        using var colour = MatChannels.Split(filled);
        Cv2.Merge([colour[0], colour[1], colour[2], channels[3]], result);
        return result;
    }

    private static Mat FillBgr(IInpaintingModel model, Mat bgr, Mat mask)
    {
        Mat result = bgr.Clone();
        try
        {
            foreach (Rect area in FindAreas(mask, bgr.Size()))
            {
                FillArea(model, result, mask, area);
            }
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The squares to fill: one per group of nearby damaged areas, each about 2.5× the damage it contains (at least a
    /// quarter of the photo's shorter side) so the network sees enough surroundings. Squares that overlap are merged.
    /// </summary>
    internal static List<Rect> FindAreas(Mat mask, Size imageSize)
    {
        var holes = new List<Rect>();
        using (Mat labels = new())
        using (Mat stats = new())
        using (Mat centroids = new())
        {
            int count = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids, PixelConnectivity.Connectivity8, MatType.CV_32S);
            int[] values = new int[count * 5];
            if (count > 0) stats.CopyDataTo(values);
            for (int i = 1; i < count; i++)
            {
                holes.Add(new Rect(values[i * 5], values[i * 5 + 1], values[i * 5 + 2], values[i * 5 + 3]));
            }
        }

        var squares = holes.Select(h => (Hole: h, Square: ContextSquare(h, imageSize))).ToList();
        bool merged = true;
        while (merged)
        {
            merged = false;
            for (int i = 0; i < squares.Count && !merged; i++)
            {
                for (int j = i + 1; j < squares.Count; j++)
                {
                    if (!squares[i].Square.IntersectsWith(squares[j].Square)) continue;
                    Rect hole = Rect.Union(squares[i].Hole, squares[j].Hole);
                    squares[i] = (hole, ContextSquare(hole, imageSize));
                    squares.RemoveAt(j);
                    merged = true;
                    break;
                }
            }
        }

        return squares.Select(s => s.Square).ToList();
    }

    private static Rect ContextSquare(Rect hole, Size imageSize)
    {
        int side = (int)Math.Ceiling(Math.Max(Math.Max(hole.Width, hole.Height) * 2.5, Math.Min(imageSize.Width, imageSize.Height) / 4.0));
        side = Math.Max(side, 32);
        int centerX = hole.X + hole.Width / 2;
        int centerY = hole.Y + hole.Height / 2;
        int x = Math.Clamp(centerX - side / 2, 0, Math.Max(0, imageSize.Width - side));
        int y = Math.Clamp(centerY - side / 2, 0, Math.Max(0, imageSize.Height - side));
        // Near a small photo's edge the square can't fit; it is cut to the photo here and padded later.
        return Rect.Intersect(new Rect(x, y, side, side), new Rect(0, 0, imageSize.Width, imageSize.Height));
    }

    private static void FillArea(IInpaintingModel model, Mat image, Mat mask, Rect area)
    {
        int side = Math.Max(area.Width, area.Height);
        int size = model.InputSize;

        using Mat crop = new(image, area);
        using Mat cropMask = new(mask, area);

        // Pad to a square (mirrored image, no damage in the padding), then scale to the network's size.
        using Mat square = new();
        Cv2.CopyMakeBorder(crop, square, 0, side - area.Height, 0, side - area.Width, BorderTypes.Reflect101, new Scalar());
        using Mat squareMask = new();
        Cv2.CopyMakeBorder(cropMask, squareMask, 0, side - area.Height, 0, side - area.Width, BorderTypes.Constant, new Scalar(0));

        using Mat input = new();
        Cv2.Resize(square, input, new Size(size, size), 0, 0, side > size ? InterpolationFlags.Area : InterpolationFlags.Cubic);
        using Mat inputMask = new();
        Cv2.Resize(squareMask, inputMask, new Size(size, size), 0, 0, InterpolationFlags.Linear);
        Cv2.Threshold(inputMask, inputMask, 0, 255, ThresholdTypes.Binary);
        // Slightly larger hole, so the damage's soft edge is regenerated too.
        using (Mat grow = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5), new Point(-1, -1)))
        {
            Cv2.Dilate(inputMask, inputMask, grow, new Point(-1, -1), 1, BorderTypes.Constant, new Scalar());
        }

        using Mat output = RunCached(model, input, inputMask);

        using Mat upscaled = new();
        Cv2.Resize(output, upscaled, new Size(side, side), 0, 0, side > size ? InterpolationFlags.Cubic : InterpolationFlags.Area);
        using Mat filled = new(upscaled, new Rect(0, 0, area.Width, area.Height));

        // Blend: fully replaced inside the painted area, fading out over a couple of pixels around it.
        using Mat weight = new();
        cropMask.ConvertTo(weight, MatType.CV_32F, 1.0 / 255.0);
        using Mat soft = new();
        using (Mat grow = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3), new Point(-1, -1)))
        {
            Cv2.Dilate(weight, soft, grow, new Point(-1, -1), 1, BorderTypes.Constant, new Scalar());
        }
        Cv2.GaussianBlur(soft, soft, new Size(0, 0), 1.0);
        Cv2.Max(soft, weight, weight);

        using Mat weight3 = new();
        Cv2.CvtColor(weight, weight3, ColorConversionCodes.GRAY2BGR);
        using Mat original = new();
        crop.ConvertTo(original, MatType.CV_32F);
        using Mat generated = new();
        filled.ConvertTo(generated, MatType.CV_32F);

        // result = original + (generated - original) * weight
        using Mat difference = new();
        Cv2.Subtract(generated, original, difference);
        Cv2.Multiply(difference, weight3, difference);
        Cv2.Add(original, difference, original);
        original.ConvertTo(crop, MatType.CV_8U);
    }

    /// <summary>Runs the network, reusing the result when the same input was filled recently.</summary>
    private static Mat RunCached(IInpaintingModel model, Mat input, Mat inputMask)
    {
        string key = Fingerprint(model, input, inputMask);
        lock (CacheLock)
        {
            for (var node = Cache.First; node != null; node = node.Next)
            {
                if (node.Value.Key != key) continue;
                Cache.Remove(node);
                Cache.AddFirst(node);
                return node.Value.Result.Clone();
            }
        }

        Mat result = model.Inpaint(input, inputMask);
        lock (CacheLock)
        {
            Cache.AddFirst((key, result.Clone()));
            while (Cache.Count > CacheCapacity)
            {
                Cache.Last!.Value.Result.Dispose();
                Cache.RemoveLast();
            }
        }
        return result;
    }

    private static string Fingerprint(IInpaintingModel model, Mat input, Mat inputMask)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] pixels = new byte[input.Rows * input.Cols * input.Channels()];
        input.CopyDataTo(pixels);
        hash.AppendData(pixels);
        byte[] maskBytes = new byte[inputMask.Rows * inputMask.Cols];
        inputMask.CopyDataTo(maskBytes);
        hash.AppendData(maskBytes);
        hash.AppendData(BitConverter.GetBytes(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(model)));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>Empties the result cache (tests).</summary>
    internal static void ClearCache()
    {
        lock (CacheLock)
        {
            foreach (var entry in Cache) entry.Result.Dispose();
            Cache.Clear();
        }
    }
}
