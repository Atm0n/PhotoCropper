using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System.Drawing;

namespace PhotoCropper.Extraction;

public static class AutoOrientationService
{
    public static Mat OrientPhoto(Mat photo)
    {
        ArgumentNullException.ThrowIfNull(photo);
        if (photo.IsEmpty || photo.Width < 50 || photo.Height < 50) return photo;

        int rotationDegrees = DetectRequiredRotation(photo);
        if (rotationDegrees == 0) return photo;

        RotateFlags flags = rotationDegrees switch
        {
            90 => RotateFlags.Rotate90Clockwise,
            180 => RotateFlags.Rotate180,
            270 => RotateFlags.Rotate90CounterClockwise,
            _ => (RotateFlags)(-1)
        };

        if ((int)flags == -1) return photo;

        Mat rotated = new();
        CvInvoke.Rotate(photo, rotated, flags);
        return rotated;
    }

    public static int DetectRequiredRotation(Mat photo)
    {
        ArgumentNullException.ThrowIfNull(photo);
        if (photo.IsEmpty || photo.Width < 50 || photo.Height < 50) return 0;

        // 1. Primary: AI Face Detection (YuNet ONNX)
        int faceRotation = FaceOrientationService.DetectFaceRotation(photo);
        if (faceRotation >= 0)
        {
            return faceRotation;
        }

        // 2. Secondary: Landscape & Scene Analysis (Sky hue and ambient lighting gradients)
        // Downscale small proxy for fast analysis (<1ms)
        int maxDim = Math.Max(photo.Width, photo.Height);
        double scale = maxDim > 300 ? 300.0 / maxDim : 1.0;
        int w = (int)Math.Round(photo.Width * scale);
        int h = (int)Math.Round(photo.Height * scale);

        using Mat small = new();
        CvInvoke.Resize(photo, small, new Size(w, h), 0, 0, Inter.Area);

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

        using Mat hsv = new();
        CvInvoke.CvtColor(bgrSmall, hsv, ColorConversion.Bgr2Hsv);

        // Analyze 4 perimeter bands (top, bottom, left, right 25%)
        int bandH = Math.Max(1, h / 4);
        int bandW = Math.Max(1, w / 4);

        using Mat topBand = new(hsv, new Rectangle(0, 0, w, bandH));
        using Mat botBand = new(hsv, new Rectangle(0, h - bandH, w, bandH));
        using Mat leftBand = new(hsv, new Rectangle(0, 0, bandW, h));
        using Mat rightBand = new(hsv, new Rectangle(w - bandW, 0, bandW, h));

        double topV = CvInvoke.Mean(topBand).V2;
        double botV = CvInvoke.Mean(botBand).V2;
        double leftV = CvInvoke.Mean(leftBand).V2;
        double rightV = CvInvoke.Mean(rightBand).V2;

        // Top sky score: Sky is bright (high V) and blue (H in [90, 130], S in [30, 220])
        double topSkyScore = ComputeSkyScore(topBand);
        double botSkyScore = ComputeSkyScore(botBand);
        double leftSkyScore = ComputeSkyScore(leftBand);
        double rightSkyScore = ComputeSkyScore(rightBand);

        // 1. Sky blue dominance check (very high confidence for outdoor photos)
        if (botSkyScore > topSkyScore + 25.0 && botSkyScore > leftSkyScore && botSkyScore > rightSkyScore)
        {
            return 180; // Upside down
        }
        if (leftSkyScore > rightSkyScore + 25.0 && leftSkyScore > topSkyScore && leftSkyScore > botSkyScore)
        {
            return 90; // Top is on the left -> rotate 90 CW
        }
        if (rightSkyScore > leftSkyScore + 25.0 && rightSkyScore > topSkyScore && rightSkyScore > botSkyScore)
        {
            return 270; // Top is on the right -> rotate 270 CW (90 CCW)
        }

        // 2. Ambient light gradient check (bright ceiling/sun vs dark floor/ground)
        // Requires strong contrast difference (>= 40 brightness units) to avoid false positives on flat scenes
        const double gradientThreshold = 40.0;
        double vertDiff = botV - topV;
        double horizDiff = leftV - rightV;

        if (vertDiff >= gradientThreshold && vertDiff > Math.Abs(horizDiff) + 15.0)
        {
            return 180; // Bottom is significantly brighter than top -> upside down
        }
        if (horizDiff >= gradientThreshold && horizDiff > Math.Abs(vertDiff) + 15.0)
        {
            return 90; // Left is significantly brighter than right -> rotate 90 CW
        }
        if (-horizDiff >= gradientThreshold && -horizDiff > Math.Abs(vertDiff) + 15.0)
        {
            return 270; // Right is significantly brighter than left -> rotate 270 CW
        }

        return 0; // Already upright or ambiguous (preserve natural scan orientation)
    }

    private static double ComputeSkyScore(Mat hsvBand)
    {
        // Blue hue in OpenCV HSV is ~90-130
        using Mat mask = new();
        using ScalarArray lower = new(new MCvScalar(85, 30, 80));
        using ScalarArray upper = new(new MCvScalar(135, 255, 255));
        CvInvoke.InRange(hsvBand, lower, upper, mask);

        int nonZero = CvInvoke.CountNonZero(mask);
        int totalPixels = Math.Max(1, hsvBand.Width * hsvBand.Height);
        return (double)nonZero / totalPixels * 100.0;
    }
}
