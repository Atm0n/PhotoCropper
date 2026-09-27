// -------------------------------------------------------------------------------------------------
// PhotoCropper - Intelligent Scanner Photo Extractor
//
// Face Detection utilizes YuNet (face_detection_yunet_2023mar.onnx).
// YuNet is developed by Shiqi Yu & OpenCV Zoo contributors (https://github.com/opencv/opencv_zoo)
// and licensed under the Apache License, Version 2.0.
// -------------------------------------------------------------------------------------------------

using Emgu.CV;
using Emgu.CV.CvEnum;
using System.Drawing;
using System.Reflection;

namespace PhotoCropper.Core.Extraction;

public static class FaceOrientationService
{
    // YuNet ONNX Output Specification (15 columns per face row):
    // 0-1: Bounding Box (X, Y)
    // 2-3: Bounding Box (Width, Height)
    // 4-5: Right Eye (X, Y)
    // 6-7: Left Eye (X, Y)
    // 8-9: Nose (X, Y)
    // 10-11: Right Corner of Mouth (X, Y)
    // 12-13: Left Corner of Mouth (X, Y)
    // 14: Confidence Score
    // private const int YuNetBboxXIndex = 0;
    // private const int YuNetBboxYIndex = 1;
    private const int YuNetBboxWidthIndex = 2;
    private const int YuNetBboxHeightIndex = 3;
    private const int YuNetRightEyeXIndex = 4;
    private const int YuNetRightEyeYIndex = 5;
    private const int YuNetLeftEyeXIndex = 6;
    private const int YuNetLeftEyeYIndex = 7;
    // private const int YuNetNoseXIndex = 8;
    private const int YuNetNoseYIndex = 9;
    private const int YuNetMouthRightXIndex = 10;
    private const int YuNetMouthRightYIndex = 11;
    private const int YuNetMouthLeftXIndex = 12;
    private const int YuNetMouthLeftYIndex = 13;
    private const int YuNetConfidenceIndex = 14;
    static FaceOrientationService()
    {
        CvInvoke.LogLevel = LogLevel.Error;
    }

    private static readonly Lazy<string?> LazyModelPath = new(ExtractModelToTemp);

    public static bool IsModelAvailable => LazyModelPath.Value != null && File.Exists(LazyModelPath.Value);

    private static string? ExtractModelToTemp()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            const string resourceName = "PhotoCropper.Core.Models.face_detection_yunet_2023mar.onnx";

            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                // Fallback to direct file search if not embedded
                string localPath = Path.Combine(AppContext.BaseDirectory, "Models", "face_detection_yunet_2023mar.onnx");
                if (File.Exists(localPath)) return localPath;
                return null;
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string modelsDir = string.IsNullOrWhiteSpace(appData)
                ? AppContext.BaseDirectory
                : Path.Combine(appData, "PhotoCropper", "Models");

            Directory.CreateDirectory(modelsDir);
            string modelPath = Path.Combine(modelsDir, "face_detection_yunet_2023mar.onnx");
            if (!File.Exists(modelPath) || new FileInfo(modelPath).Length != stream.Length)
            {
                using FileStream fileStream = File.Create(modelPath);
                stream.CopyTo(fileStream);
            }
            return modelPath;
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

        try
        {
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
                float score = data[i, YuNetConfidenceIndex];
                if (score < scoreThreshold) continue;

                float faceW = data[i, YuNetBboxWidthIndex];
                float faceH = data[i, YuNetBboxHeightIndex];

                float rightEyeX = data[i, YuNetRightEyeXIndex];
                float rightEyeY = data[i, YuNetRightEyeYIndex];
                float leftEyeX = data[i, YuNetLeftEyeXIndex];
                float leftEyeY = data[i, YuNetLeftEyeYIndex];

                float noseY = data[i, YuNetNoseYIndex];

                float mouthRightX = data[i, YuNetMouthRightXIndex];
                float mouthRightY = data[i, YuNetMouthRightYIndex];
                float mouthLeftX = data[i, YuNetMouthLeftXIndex];
                float mouthLeftY = data[i, YuNetMouthLeftYIndex];

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
        catch
        {
            return 0f;
        }
    }
}
