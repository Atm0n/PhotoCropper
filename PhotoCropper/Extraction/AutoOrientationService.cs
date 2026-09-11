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

        // 2. Secondary: Landscape & Scene Analysis (4-way multi-feature evaluation)
        return DetectLandscapeRotation(photo);
    }

    public static int DetectLandscapeRotation(Mat photo)
    {
        ArgumentNullException.ThrowIfNull(photo);
        if (photo.IsEmpty || photo.Width < 50 || photo.Height < 50) return 0;

        // Downscale small proxy for fast analysis (<1ms per rotation)
        int maxDim = Math.Max(photo.Width, photo.Height);
        double scale = maxDim > 240 ? 240.0 / maxDim : 1.0;
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

        int[] rotations = [0, 90, 180, 270];
        double bestScore = double.MinValue;
        double secondBestScore = double.MinValue;
        int bestRot = 0;

        foreach (int rot in rotations)
        {
            double score = ScoreLandscapeRotation(bgrSmall, rot);
            if (score > bestScore)
            {
                secondBestScore = bestScore;
                bestScore = score;
                bestRot = rot;
            }
            else if (score > secondBestScore)
            {
                secondBestScore = score;
            }
        }

        // Only rotate if the top candidate has a clear positive score and outscores alternatives
        if (bestScore >= 8.0 && (bestScore - secondBestScore) >= 4.0)
        {
            return bestRot;
        }

        return 0; // Ambiguous scene -> preserve natural scanner bed placement
    }

    private static double ScoreLandscapeRotation(Mat bgrSmall, int rot)
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

        int cw = candidate.Width;
        int ch = candidate.Height;
        int bandH = Math.Max(2, (int)Math.Round(ch * 0.38));

        using Mat hsv = new();
        CvInvoke.CvtColor(candidate, hsv, ColorConversion.Bgr2Hsv);

        using Mat gray = new();
        CvInvoke.CvtColor(candidate, gray, ColorConversion.Bgr2Gray);

        // Top and bottom region bounding boxes
        Rectangle topRect = new(0, 0, cw, bandH);
        Rectangle botRect = new(0, ch - bandH, cw, bandH);

        using Mat topHsv = new(hsv, topRect);
        using Mat botHsv = new(hsv, botRect);
        using Mat topGray = new(gray, topRect);
        using Mat botGray = new(gray, botRect);

        // 1. Sky feature: Blue sky + Overcast/Cloudy bright sky
        double topSky = ComputeSkyRatio(topHsv);
        double botSky = ComputeSkyRatio(botHsv);

        // 2. Vegetation feature: Foliage and grass (Hue in green band)
        double topVeg = ComputeVegetationRatio(topHsv);
        double botVeg = ComputeVegetationRatio(botHsv);

        // 3. Water feature: Ocean, lake, river, pool, sea (cyan/blue/teal darker than sky)
        double topWater = ComputeWaterRatio(topHsv);
        double botWater = ComputeWaterRatio(botHsv);

        // 4. Luminance (brightness)
        double topV = CvInvoke.Mean(topHsv).V2;
        double botV = CvInvoke.Mean(botHsv).V2;

        // 5. Texture complexity (Laplacian variance: sky is smooth, terrain/objects are textured)
        double topTex = ComputeTextureVariance(topGray);
        double botTex = ComputeTextureVariance(botGray);

        double totalSky = topSky + botSky;
        double totalVeg = topVeg + botVeg;
        double totalWater = topWater + botWater;

        // Only outdoor scenes with perceptible sky, vegetation, or water are evaluated
        if (totalSky < 6.0 && totalVeg < 6.0 && totalWater < 6.0)
        {
            return 0.0;
        }

        double skyDelta = topSky - botSky;          // Sky at top -> positive
        double vegDelta = botVeg - topVeg;          // Vegetation at bottom -> positive
        double waterDelta = botWater - topWater;    // Water at bottom -> positive
        double lumDelta = topV - botV;              // Light coming from above -> positive
        double texDelta = botTex - topTex;          // High detail ground at bottom -> positive

        return (skyDelta * 2.5) + (vegDelta * 1.8) + (waterDelta * 1.6) + (lumDelta * 0.3) + (texDelta * 0.4);
    }

    private static double ComputeSkyRatio(Mat hsvRegion)
    {
        // Blue Sky: H in [75, 140], S >= 15, V >= 80
        using Mat blueMask = new();
        using ScalarArray blueLow = new(new MCvScalar(75, 15, 80));
        using ScalarArray blueHigh = new(new MCvScalar(140, 255, 255));
        CvInvoke.InRange(hsvRegion, blueLow, blueHigh, blueMask);

        // Overcast / Cloudy White Sky: S <= 45, V >= 165
        using Mat cloudMask = new();
        using ScalarArray cloudLow = new(new MCvScalar(0, 0, 165));
        using ScalarArray cloudHigh = new(new MCvScalar(180, 45, 255));
        CvInvoke.InRange(hsvRegion, cloudLow, cloudHigh, cloudMask);

        using Mat combinedSky = new();
        CvInvoke.BitwiseOr(blueMask, cloudMask, combinedSky);

        int count = CvInvoke.CountNonZero(combinedSky);
        int total = Math.Max(1, hsvRegion.Width * hsvRegion.Height);
        return (double)count / total * 100.0;
    }

    private static double ComputeWaterRatio(Mat hsvRegion)
    {
        // Ocean / Sea / Lake / Pool: Cyan/Deep-Blue/Teal (Hue in [65, 135], Sat >= 30, Val in [25, 175])
        // Distinguishes rich/darker body of water from bright/high-luminance sky
        using Mat waterMask = new();
        using ScalarArray waterLow = new(new MCvScalar(65, 30, 25));
        using ScalarArray waterHigh = new(new MCvScalar(135, 255, 175));
        CvInvoke.InRange(hsvRegion, waterLow, waterHigh, waterMask);

        int count = CvInvoke.CountNonZero(waterMask);
        int total = Math.Max(1, hsvRegion.Width * hsvRegion.Height);
        return (double)count / total * 100.0;
    }

    private static double ComputeVegetationRatio(Mat hsvRegion)
    {
        // Green foliage / grass: H in [30, 88], S >= 25, V >= 25
        using Mat vegMask = new();
        using ScalarArray vegLow = new(new MCvScalar(30, 25, 25));
        using ScalarArray vegHigh = new(new MCvScalar(88, 255, 255));
        CvInvoke.InRange(hsvRegion, vegLow, vegHigh, vegMask);

        int count = CvInvoke.CountNonZero(vegMask);
        int total = Math.Max(1, hsvRegion.Width * hsvRegion.Height);
        return (double)count / total * 100.0;
    }

    private static double ComputeTextureVariance(Mat grayRegion)
    {
        using Mat laplacian = new();
        CvInvoke.Laplacian(grayRegion, laplacian, DepthType.Cv64F);

        MCvScalar mean = new();
        MCvScalar stdDev = new();
        CvInvoke.MeanStdDev(laplacian, ref mean, ref stdDev);

        return Math.Min(100.0, stdDev.V0);
    }
}
