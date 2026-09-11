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
    public static int DetectFaceRotation(Mat photo, float scoreThreshold = 0.6f)
    {
        ArgumentNullException.ThrowIfNull(photo);
        if (photo.IsEmpty || photo.Width < 60 || photo.Height < 60) return -1;
        if (LazyModelPath.Value == null || !File.Exists(LazyModelPath.Value)) return -1;

        // Resize image to max 320px for fast face detection (<5ms)
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

        using var detector = new FaceDetectorYN(
            LazyModelPath.Value,
            string.Empty,
            new Size(w, h),
            scoreThreshold,
            0.3f,
            5000);

        using Mat faces = new();
        detector.Detect(bgrSmall, faces);

            if (faces.IsEmpty || faces.Rows == 0) return -1;

            int votes0 = 0, votes90 = 0, votes180 = 0, votes270 = 0;

            float[,] data = (float[,])faces.GetData();
            for (int i = 0; i < faces.Rows; i++)
            {
                float score = data[i, 14];
                if (score < scoreThreshold) continue;

                float rightEyeX = data[i, 4];
                float rightEyeY = data[i, 5];
                float leftEyeX = data[i, 6];
                float leftEyeY = data[i, 7];

                float mouthRightX = data[i, 10];
                float mouthRightY = data[i, 11];
                float mouthLeftX = data[i, 12];
                float mouthLeftY = data[i, 13];

                float eyeMidX = (rightEyeX + leftEyeX) * 0.5f;
                float eyeMidY = (rightEyeY + leftEyeY) * 0.5f;

                float mouthMidX = (mouthRightX + mouthLeftX) * 0.5f;
                float mouthMidY = (mouthRightY + mouthLeftY) * 0.5f;

                // Vector from mouth to eyes points UPWARDS in face coordinates
                float upVectorX = eyeMidX - mouthMidX;
                float upVectorY = eyeMidY - mouthMidY;

                // Calculate angle of the 'up' vector relative to image coordinate system:
                // Standard upright image: Eyes are above mouth (smaller Y), so upVectorY is negative (pointing up).
                // Angle in degrees: 0° is up (0, -1), 90° CW is right (1, 0), 180° is down (0, 1), 270° CW is left (-1, 0).
                double angleRad = Math.Atan2(upVectorX, -upVectorY);
                double angleDeg = angleRad * (180.0 / Math.PI);
                if (angleDeg < 0) angleDeg += 360.0;

                // Snap to nearest 90 degree quadrant
                int snapAngle = ((int)Math.Round(angleDeg / 90.0) * 90) % 360;

                switch (snapAngle)
                {
                    case 0:
                        votes0++;
                        break;
                    case 90:
                        // If face UP vector points right (90°), image must be rotated 270° CW to make it upright
                        votes270++;
                        break;
                    case 180:
                        votes180++;
                        break;
                    case 270:
                        // If face UP vector points left (270°), image must be rotated 90° CW to make it upright
                        votes90++;
                        break;
                }
            }

        int maxVotes = Math.Max(Math.Max(votes0, votes90), Math.Max(votes180, votes270));
        if (maxVotes == 0) return -1;

        if (votes0 == maxVotes) return 0;
        if (votes90 == maxVotes) return 90;
        if (votes180 == maxVotes) return 180;
        if (votes270 == maxVotes) return 270;

        return -1;
    }
}
