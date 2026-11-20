// -------------------------------------------------------------------------------------------------
// PhotoCropper - Intelligent Scanner Photo Extractor
//
// Face Detection utilizes YuNet (face_detection_yunet_2023mar.onnx).
// YuNet is developed by Shiqi Yu & OpenCV Zoo contributors (https://github.com/opencv/opencv_zoo)
// and licensed under the Apache License, Version 2.0.
// -------------------------------------------------------------------------------------------------

using OpenCvSharp;
using PhotoCropper.Core.Common;
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
        Cv2.SetLogLevel(LogLevel.ERROR);
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
                WriteModelAtomically(stream, modelPath);
            }
            return modelPath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the model to a unique temporary file and then renames it into place. The GUI and CLI can start
    /// at the same time; writing <paramref name="modelPath"/> directly would let one process load a model the
    /// other is still writing. A rename on the same volume is atomic, so readers see the old file or the
    /// complete new one.
    /// </summary>
    internal static void WriteModelAtomically(Stream source, string modelPath)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(modelPath);

        string tempPath = $"{modelPath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (FileStream fileStream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(fileStream);
            }

            try
            {
                File.Move(tempPath, modelPath, overwrite: true);
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && File.Exists(modelPath) && new FileInfo(modelPath).Length == new FileInfo(tempPath).Length)
            {
                // Another process put an identical model in place first and has it open; use theirs.
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Leftover temp file is harmless.
            }
        }
    }

    /// <summary>
    /// Idle detectors, reused across photos. Creating a FaceDetectorYN parses the ONNX model, which previously
    /// happened four times per photo. A detector isn't thread-safe, so each one is rented by a single call at a
    /// time; parallel extraction rents several.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentBag<FaceDetectorYN> IdleDetectors = [];

    /// <summary>At most this many idle detectors are kept; extras are disposed when returned.</summary>
    internal static int MaxIdleDetectors { get; } = Math.Max(1, Environment.ProcessorCount);

    internal static int IdleDetectorCount => IdleDetectors.Count;

    private static FaceDetectorYN? RentDetector() =>
        IdleDetectors.TryTake(out var detector) ? detector : CreateDetector();

    private static void ReturnDetector(FaceDetectorYN detector)
    {
        // The count check and Add aren't atomic, so the pool may briefly exceed the cap by a few under
        // contention; it stays bounded by the number of concurrent callers.
        if (IdleDetectors.Count < MaxIdleDetectors)
        {
            IdleDetectors.Add(detector);
        }
        else
        {
            detector.Dispose();
        }
    }

    /// <summary>Frees all idle detectors (their native model memory). Detectors in use are freed when returned.</summary>
    public static void ReleaseCachedDetectors()
    {
        while (IdleDetectors.TryTake(out var detector))
        {
            detector.Dispose();
        }
    }

    private static FaceDetectorYN? CreateDetector()
    {
        string? modelPath = LazyModelPath.Value;
        if (modelPath == null || !File.Exists(modelPath)) return null;

        try
        {
            // Input size and score threshold are set per call before detecting.
            return FaceDetectorYN.Create(modelPath, string.Empty, new Size(320, 320), 0.45f, 0.3f, 5000);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"[PhotoCropper] Could not load face detection model: {ex.Message}");
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
        if (photo.Empty() || photo.Width < 60 || photo.Height < 60) return -1;
        if (LazyModelPath.Value == null || !File.Exists(LazyModelPath.Value)) return -1;

        // Resize image to max 320px for fast face detection (<2ms per pass)
        int maxDim = Math.Max(photo.Width, photo.Height);
        double scale = maxDim > 320 ? 320.0 / maxDim : 1.0;
        int w = (int)Math.Round(photo.Width * scale);
        int h = (int)Math.Round(photo.Height * scale);

        using Mat small = new();
        Cv2.Resize(photo, small, new Size(w, h), 0, 0, InterpolationFlags.Linear);

        using Mat bgrSmall = new();
        if (small.Channels() == 4)
        {
            Cv2.CvtColor(small, bgrSmall, ColorConversionCodes.BGRA2BGR);
        }
        else if (small.Channels() == 1)
        {
            Cv2.CvtColor(small, bgrSmall, ColorConversionCodes.GRAY2BGR);
        }
        else
        {
            small.CopyTo(bgrSmall);
        }

        // Test the 4 cardinal rotations to find the orientation where faces are upright
        var detector = RentDetector();
        if (detector == null) return -1;

        try
        {
            float[] scores = [.. Rotations.Select(rot => ScoreRotation(detector, bgrSmall, rot, scoreThreshold))];
            return ChooseFaceRotation(scores, scoreThreshold);
        }
        finally
        {
            ReturnDetector(detector);
        }
    }

    private static readonly int[] Rotations = [0, 90, 180, 270];

    /// <summary>
    /// Picks the clockwise turn whose upright faces score highest, from the scores of turning the photo by 0, 90, 180
    /// and 270 degrees; -1 when no turn has a face worth trusting.
    /// </summary>
    internal static int ChooseFaceRotation(IReadOnlyList<float> scores, float scoreThreshold)
    {
        ArgumentNullException.ThrowIfNull(scores);
        int bestRot = -1;
        float bestScore = scoreThreshold;
        for (int i = 0; i < Rotations.Length; i++)
        {
            if (scores[i] > bestScore)
            {
                bestScore = scores[i];
                bestRot = Rotations[i];
            }
        }

        // A single weak "face" found only when the photo is turned (a pattern in a crowd, foliage or fruit) is
        // not enough to turn it: real faces score well above this, and upright is the safer guess.
        if (bestRot > 0 && bestScore < MinimumScoreToTurn) return -1;
        return bestRot;
    }

    /// <summary>The lowest total face score that turns a photo away from how it was placed.</summary>
    internal const float MinimumScoreToTurn = 0.6f;

    private static float ScoreRotation(FaceDetectorYN detector, Mat bgrSmall, int rot, float scoreThreshold)
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
                270 => RotateFlags.Rotate90Counterclockwise,
                _ => RotateFlags.Rotate180
            };
            Cv2.Rotate(bgrSmall, candidate, flag);
        }

        try
        {
            detector.SetInputSize(candidate.Size());
            detector.SetScoreThreshold(scoreThreshold);

            using Mat faces = new();
            detector.Detect(candidate, faces);

            return ScoreFaceData(faces, scoreThreshold);
        }
        catch
        {
            return 0f;
        }
    }

    internal static float ScoreFaceData(Mat faces, float scoreThreshold)
    {
        if (faces.Empty()) return 0f;
        int rows = faces.Rows;
        int cols = faces.Cols;
        if (rows == 0) return 0f;

        // One row per face: box, five landmarks, confidence (see the YuNet indices above).
        float[] values = new float[rows * cols];
        faces.CopyDataTo(values);
        float totalScore = 0f;

        for (int i = 0; i < rows; i++)
        {
            float score = values[i * cols + YuNetConfidenceIndex];
            if (score < scoreThreshold) continue;

            float faceW = values[i * cols + YuNetBboxWidthIndex];
            float faceH = values[i * cols + YuNetBboxHeightIndex];

            float rightEyeX = values[i * cols + YuNetRightEyeXIndex];
            float rightEyeY = values[i * cols + YuNetRightEyeYIndex];
            float leftEyeX = values[i * cols + YuNetLeftEyeXIndex];
            float leftEyeY = values[i * cols + YuNetLeftEyeYIndex];

            float noseY = values[i * cols + YuNetNoseYIndex];

            float mouthRightX = values[i * cols + YuNetMouthRightXIndex];
            float mouthRightY = values[i * cols + YuNetMouthRightYIndex];
            float mouthLeftX = values[i * cols + YuNetMouthLeftXIndex];
            float mouthLeftY = values[i * cols + YuNetMouthLeftYIndex];

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
