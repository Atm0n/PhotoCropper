using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Dnn;
using Emgu.CV.Structure;
using System.Drawing;
using System.Reflection;

namespace PhotoCropper.Extraction;

public static class FaceOrientationService
{
    private static readonly Lazy<string?> LazyModelPath = new(ExtractModelToTemp);

    public static bool IsModelAvailable => LazyModelPath.Value != null && File.Exists(LazyModelPath.Value);

    private static string? ExtractModelToTemp()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            const string resourceName = "PhotoCropper.Models.face_detection_yunet_2023mar.onnx";

            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                // Fallback to direct file search if not embedded
                string localPath = Path.Combine(AppContext.BaseDirectory, "Models", "face_detection_yunet_2023mar.onnx");
                if (File.Exists(localPath)) return localPath;
                return null;
            }

            string tempPath = Path.Combine(Path.GetTempPath(), "face_detection_yunet_2023mar.onnx");
            if (!File.Exists(tempPath) || new FileInfo(tempPath).Length != stream.Length)
            {
                using FileStream fileStream = File.Create(tempPath);
                stream.CopyTo(fileStream);
            }
            return tempPath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Detects the required clockwise rotation (0, 90, 180, 270) to make detected faces upright.
    /// Returns -1 if no faces are detected with sufficient confidence.
    /// </summary>
    public static int DetectFaceRotation(Mat photo, float scoreThreshold = 0.45f)
    {
        ArgumentNullException.ThrowIfNull(photo);
        if (photo.IsEmpty || photo.Width < 60 || photo.Height < 60) return -1;
        if (LazyModelPath.Value == null || !File.Exists(LazyModelPath.Value)) return -1;

        // Resize image to max 320px for fast face detection (<2ms per pass)
        int maxDim = Math.Max(photo.Width, photo.Height);
        double scale = maxDim > 320 ? 320.0 / maxDim : 1.0;
        int w = (int)Math.Round(photo.Width * scale);
        int h = (int)Math.Round(photo.Height * scale);

        using Mat small = new();
        CvInvoke.Resize(photo, small, new Size(w, h), 0, 0, Inter.Linear);

        using Mat bgrSmall = new();
        if (small.NumberOfChannels == 4)
        {
            CvInvoke.CvtColor(small, bgrSmall, ColorConversion.Bgra2Bgr);
        }
        else if (small.NumberOfChannels == 1)
        {
            CvInvoke.CvtColor(small, bgrSmall, ColorConversion.Gray2Bgr);
        }
        else
        {
            small.CopyTo(bgrSmall);
        }

        // Test the 4 cardinal rotations to find the orientation where faces are upright
        int[] rotations = [0, 90, 180, 270];
        int bestRot = -1;
        float bestScore = scoreThreshold;

        foreach (int rot in rotations)
        {
            float score = ScoreRotation(bgrSmall, rot, scoreThreshold);
            if (score > bestScore)
            {
                bestScore = score;
                bestRot = rot;
            }
        }

        return bestRot;
    }

    private static float ScoreRotation(Mat bgrSmall, int rot, float scoreThreshold)
    {
        using Mat candidate = new();
        if (rot == 0)
        {
            bgrSmall.CopyTo(candidate);
        }
        else
        {
            RotateFlags flag = rot switch
            {
                90 => RotateFlags.Rotate90Clockwise,
                180 => RotateFlags.Rotate180,
                270 => RotateFlags.Rotate90CounterClockwise,
                _ => RotateFlags.Rotate180
            };
            CvInvoke.Rotate(bgrSmall, candidate, flag);
        }

        using var detector = new FaceDetectorYN(
            LazyModelPath.Value,
            string.Empty,
            candidate.Size,
            scoreThreshold,
            0.3f,
            5000);

        using Mat faces = new();
        detector.Detect(candidate, faces);

        if (faces.IsEmpty || faces.Rows == 0) return 0f;

        float[,] data = (float[,])faces.GetData();
        float totalScore = 0f;

        for (int i = 0; i < faces.Rows; i++)
        {
            float score = data[i, 14];
            if (score < scoreThreshold) continue;

            float faceW = data[i, 2];
            float faceH = data[i, 3];

            float rightEyeX = data[i, 4];
            float rightEyeY = data[i, 5];
            float leftEyeX = data[i, 6];
            float leftEyeY = data[i, 7];

            float noseY = data[i, 9];

            float mouthRightX = data[i, 10];
            float mouthRightY = data[i, 11];
            float mouthLeftX = data[i, 12];
            float mouthLeftY = data[i, 13];

            float eyeMidY = (rightEyeY + leftEyeY) * 0.5f;
            float mouthMidY = (mouthRightY + mouthLeftY) * 0.5f;

            // 1. Upright vertical sequence: Eyes are above Nose, Nose is above Mouth
            float eyeToMouthDist = mouthMidY - eyeMidY;
            if (eyeToMouthDist < faceH * 0.12f) continue;
            if (noseY <= eyeMidY || noseY >= mouthMidY) continue;

            // 2. Eyes must be predominantly horizontal (not vertically stacked as in sideways faces)
            float eyeHorizSpan = Math.Abs(leftEyeX - rightEyeX);
            float eyeVertSpan = Math.Abs(leftEyeY - rightEyeY);
            if (eyeHorizSpan < eyeVertSpan * 1.5f) continue;

            // 3. Mouth corners must be predominantly horizontal
            float mouthHorizSpan = Math.Abs(mouthLeftX - mouthRightX);
            float mouthVertSpan = Math.Abs(mouthLeftY - mouthRightY);
            if (mouthHorizSpan < mouthVertSpan * 1.5f) continue;

            // 4. Eye line tilt relative to horizon must be within ±35 degrees
            double eyeTiltDeg = Math.Abs(Math.Atan2(eyeVertSpan, Math.Max(1f, eyeHorizSpan)) * (180.0 / Math.PI));
            if (eyeTiltDeg > 35.0) continue;

            totalScore += score;
        }

        return totalScore;
    }
}
