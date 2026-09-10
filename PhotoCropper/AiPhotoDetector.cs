using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Drawing;

namespace PhotoCropper;

public sealed class AiPhotoDetector : IDisposable
{
    private readonly InferenceSession? _session;
    private readonly bool _isModelLoaded;

    public bool IsModelLoaded => _isModelLoaded;

    public AiPhotoDetector(string? modelPath = null)
    {
        string path = modelPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "photo_detector.onnx");
        if (File.Exists(path))
        {
            try
            {
                using var sessionOptions = new SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                    ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
                };
                _session = new InferenceSession(path, sessionOptions);
                _isModelLoaded = true;
            }
            catch
            {
                _isModelLoaded = false;
            }
        }
    }

    public System.Collections.ObjectModel.Collection<RotatedRect> Detect(Mat source, float confidenceThreshold = 0.40f, float iouThreshold = 0.45f)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!_isModelLoaded || _session == null || source.IsEmpty)
        {
            return [];
        }

        const int targetSize = 640;
        
        // 1. Calculate Letterbox padding and scaling
        float scale = Math.Min((float)targetSize / source.Width, (float)targetSize / source.Height);
        int newUnpadW = (int)Math.Round(source.Width * scale);
        int newUnpadH = (int)Math.Round(source.Height * scale);

        int padX = (targetSize - newUnpadW) / 2;
        int padY = (targetSize - newUnpadH) / 2;

        using Mat resized = new();
        CvInvoke.Resize(source, resized, new Size(newUnpadW, newUnpadH), 0, 0, Inter.Linear);

        using Mat letterboxed = new(targetSize, targetSize, DepthType.Cv8U, 3);
        letterboxed.SetTo(new MCvScalar(114, 114, 114)); // Standard YOLO gray letterbox padding

        Rectangle destRoi = new(padX, padY, newUnpadW, newUnpadH);
        using Mat canvasSub = new(letterboxed, destRoi);
        resized.CopyTo(canvasSub);

        // 2. Prepare RGB Normalized Float Tensor [1, 3, 640, 640]
        using Mat rgb = new();
        CvInvoke.CvtColor(letterboxed, rgb, ColorConversion.Bgr2Rgb);

        var tensor = new DenseTensor<float>([1, 3, targetSize, targetSize]);
        byte[] rgbData = new byte[targetSize * targetSize * 3];
        rgb.CopyTo(rgbData);

        for (int y = 0; y < targetSize; y++)
        {
            for (int x = 0; x < targetSize; x++)
            {
                int pixelIndex = (y * targetSize + x) * 3;
                tensor[0, 0, y, x] = rgbData[pixelIndex] / 255.0f;     // R
                tensor[0, 1, y, x] = rgbData[pixelIndex + 1] / 255.0f; // G
                tensor[0, 2, y, x] = rgbData[pixelIndex + 2] / 255.0f; // B
            }
        }

        // 3. Run Inference
        string inputName = _session.InputNames[0];
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(inputName, tensor)
        };

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(inputs);
        var outputTensor = results[0].AsTensor<float>();

        // 4. Parse YOLO-OBB Output: shape [1, 6, 8400] or [1, 8400, 6] (cx, cy, w, h, score, angle_radians)
        var rawBoxes = ParseYoloObbOutput(outputTensor, confidenceThreshold, scale, padX, padY, source.Size);

        // 5. Apply Non-Maximum Suppression (NMS)
        return ApplyRotatedNms(rawBoxes, iouThreshold);
    }

    private static List<RotatedBoxPrediction> ParseYoloObbOutput(
        Tensor<float> output, 
        float minConfidence, 
        float scale, 
        int padX, 
        int padY, 
        Size originalSize)
    {
        var predictions = new List<RotatedBoxPrediction>();

        int dims0 = output.Dimensions[0]; // 1
        int dims1 = output.Dimensions[1];
        int dims2 = output.Dimensions[2];

        // Format A: [1, 4 + C + 1, N] where C is number of classes (e.g. 1 class -> 6 channels, 15 classes -> 20 channels)
        if (dims1 >= 6 && dims2 > dims1)
        {
            int numPredictions = dims2;
            int numClasses = dims1 - 5; // 4 bbox coords + C classes + 1 angle
            int angleChannel = dims1 - 1;

            for (int i = 0; i < numPredictions; i++)
            {
                // Find maximum class confidence
                float maxScore = 0f;
                for (int c = 0; c < numClasses; c++)
                {
                    float score = output[0, 4 + c, i];
                    if (score > maxScore) maxScore = score;
                }

                if (maxScore < minConfidence) continue;

                float cx = (output[0, 0, i] - padX) / scale;
                float cy = (output[0, 1, i] - padY) / scale;
                float w = output[0, 2, i] / scale;
                float h = output[0, 3, i] / scale;
                float angleRad = output[0, angleChannel, i];
                float angleDeg = (float)(angleRad * 180.0 / Math.PI);

                cx = Math.Clamp(cx, 0, originalSize.Width);
                cy = Math.Clamp(cy, 0, originalSize.Height);

                predictions.Add(new RotatedBoxPrediction(new PointF(cx, cy), new SizeF(w, h), angleDeg, maxScore));
            }
        }
        // Format B: [1, N, 4 + C + 1]
        else if (dims2 >= 6)
        {
            int numPredictions = dims1;
            int numClasses = dims2 - 5;
            int angleIndex = dims2 - 1;

            for (int i = 0; i < numPredictions; i++)
            {
                float maxScore = 0f;
                for (int c = 0; c < numClasses; c++)
                {
                    float score = output[0, i, 4 + c];
                    if (score > maxScore) maxScore = score;
                }

                if (maxScore < minConfidence) continue;

                float cx = (output[0, i, 0] - padX) / scale;
                float cy = (output[0, i, 1] - padY) / scale;
                float w = output[0, i, 2] / scale;
                float h = output[0, i, 3] / scale;
                float angleRad = output[0, i, angleIndex];
                float angleDeg = (float)(angleRad * 180.0 / Math.PI);

                cx = Math.Clamp(cx, 0, originalSize.Width);
                cy = Math.Clamp(cy, 0, originalSize.Height);

                predictions.Add(new RotatedBoxPrediction(new PointF(cx, cy), new SizeF(w, h), angleDeg, maxScore));
            }
        }

        return predictions;
    }

    private static System.Collections.ObjectModel.Collection<RotatedRect> ApplyRotatedNms(List<RotatedBoxPrediction> boxes, float iouThreshold)
    {
        var sorted = boxes.OrderByDescending(b => b.Score).ToList();
        var selected = new System.Collections.ObjectModel.Collection<RotatedRect>();

        while (sorted.Count > 0)
        {
            var best = sorted[0];
            var bestRect = new RotatedRect(best.Center, best.Size, best.Angle);
            selected.Add(bestRect);
            sorted.RemoveAt(0);

            sorted.RemoveAll(candidate =>
            {
                var candidateRect = new RotatedRect(candidate.Center, candidate.Size, candidate.Angle);
                return ComputeRotatedIou(bestRect, candidateRect) > iouThreshold;
            });
        }

        return selected;
    }

    private static float ComputeRotatedIou(RotatedRect a, RotatedRect b)
    {
        using var polyA = new Emgu.CV.Util.VectorOfPoint(Array.ConvertAll(a.GetVertices(), Point.Round));
        using var polyB = new Emgu.CV.Util.VectorOfPoint(Array.ConvertAll(b.GetVertices(), Point.Round));

        double areaA = a.Size.Width * a.Size.Height;
        double areaB = b.Size.Width * b.Size.Height;
        if (areaA <= 0 || areaB <= 0) return 0f;

        // Approximate IOU with intersection area
        Rectangle boundA = CvInvoke.BoundingRectangle(polyA);
        Rectangle boundB = CvInvoke.BoundingRectangle(polyB);
        Rectangle intersection = Rectangle.Intersect(boundA, boundB);

        if (intersection.IsEmpty || intersection.Width <= 0 || intersection.Height <= 0)
        {
            return 0f;
        }

        float interArea = intersection.Width * intersection.Height;
        return interArea / (float)(areaA + areaB - interArea);
    }

    public void Dispose()
    {
        _session?.Dispose();
    }

    private readonly record struct RotatedBoxPrediction(PointF Center, SizeF Size, float Angle, float Score);
}
