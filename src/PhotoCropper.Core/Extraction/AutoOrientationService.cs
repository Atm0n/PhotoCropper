using OpenCvSharp;

namespace PhotoCropper.Core.Extraction;

public static class AutoOrientationService
{
    public static Mat OrientPhoto(Mat photo)
    {
        ArgumentNullException.ThrowIfNull(photo);
        if (photo.Empty() || photo.Width < 50 || photo.Height < 50) return photo;

        int rotationDegrees = DetectRequiredRotation(photo);
        if (rotationDegrees == 0) return photo;

        RotateFlags flags = rotationDegrees switch
        {
            90 => RotateFlags.Rotate90Clockwise,
            180 => RotateFlags.Rotate180,
            270 => RotateFlags.Rotate90Counterclockwise,
            _ => (RotateFlags)(-1)
        };

        if ((int)flags == -1) return photo;

        Mat rotated = new();
        Cv2.Rotate(photo, rotated, flags);
        return rotated;
    }

    public static int DetectRequiredRotation(Mat photo)
    {
        ArgumentNullException.ThrowIfNull(photo);
        if (photo.Empty() || photo.Width < 50 || photo.Height < 50) return 0;

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
        if (photo.Empty() || photo.Width < 50 || photo.Height < 50) return 0;

        // Downscale small proxy for fast analysis (<1ms per rotation)
        int maxDim = Math.Max(photo.Width, photo.Height);
        double scale = maxDim > 240 ? 240.0 / maxDim : 1.0;
        int w = (int)Math.Round(photo.Width * scale);
        int h = (int)Math.Round(photo.Height * scale);

        using Mat small = new();
        Cv2.Resize(photo, small, new Size(w, h), 0, 0, InterpolationFlags.Area);

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

        double[] scores = [.. Rotations.Select(rot => ScoreLandscapeRotation(bgrSmall, rot))];
        return ChooseLandscapeRotation(scores);
    }

    private static readonly int[] Rotations = [0, 90, 180, 270];

    /// <summary>
    /// Picks the clockwise turn from the scene scores of turning the photo by 0, 90, 180 and 270 degrees, or 0 when
    /// none is convincing.
    /// </summary>
    internal static int ChooseLandscapeRotation(IReadOnlyList<double> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);
        double bestScore = double.MinValue;
        double secondBestScore = double.MinValue;
        int bestRot = 0;

        for (int i = 0; i < Rotations.Length; i++)
        {
            double score = scores[i];
            if (score > bestScore)
            {
                secondBestScore = bestScore;
                bestScore = score;
                bestRot = Rotations[i];
            }
            else if (score > secondBestScore)
            {
                secondBestScore = score;
            }
        }

        // Photos are usually stuck in upright, and turning an upright photo the wrong way is worse than leaving a
        // sideways one alone, so only a strong, clear-cut score turns it: real skies over land score 60-270 here,
        // while the wrong turns these features suggest for upright photos (city streets, crowds, interiors) stay
        // below about 30 or barely beat the upright score.
        if (bestScore >= MinimumLandscapeScore && (bestScore - secondBestScore) >= bestScore * MinimumLandscapeLead)
        {
            return bestRot;
        }

        return 0; // Ambiguous scene -> preserve natural scanner bed placement
    }

    /// <summary>The lowest landscape score that turns a photo.</summary>
    internal const double MinimumLandscapeScore = 40.0;

    /// <summary>How far ahead of the runner-up the best turn must be, as a share of its score.</summary>
    internal const double MinimumLandscapeLead = 1.0 / 3.0;

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
                270 => RotateFlags.Rotate90Counterclockwise,
                _ => RotateFlags.Rotate180
            };
            Cv2.Rotate(bgrSmall, candidate, flag);
        }

        int cw = candidate.Width;
        int ch = candidate.Height;
        int bandH = Math.Max(2, (int)Math.Round(ch * 0.38));

        using Mat hsv = new();
        Cv2.CvtColor(candidate, hsv, ColorConversionCodes.BGR2HSV);

        using Mat gray = new();
        Cv2.CvtColor(candidate, gray, ColorConversionCodes.BGR2GRAY);

        // Top and bottom region bounding boxes
        Rect topRect = new(0, 0, cw, bandH);
        Rect botRect = new(0, ch - bandH, cw, bandH);

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
        double topV = Cv2.Mean(topHsv).Val2;
        double botV = Cv2.Mean(botHsv).Val2;

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
        Scalar blueLow = new Scalar(75, 15, 80);
        Scalar blueHigh = new Scalar(140, 255, 255);
        Cv2.InRange(hsvRegion, blueLow, blueHigh, blueMask);

        // Overcast / Cloudy White Sky: S <= 45, V >= 165
        using Mat cloudMask = new();
        Scalar cloudLow = new Scalar(0, 0, 165);
        Scalar cloudHigh = new Scalar(180, 45, 255);
        Cv2.InRange(hsvRegion, cloudLow, cloudHigh, cloudMask);

        using Mat combinedSky = new();
        Cv2.BitwiseOr(blueMask, cloudMask, combinedSky);

        int count = Cv2.CountNonZero(combinedSky);
        int total = Math.Max(1, hsvRegion.Width * hsvRegion.Height);
        return (double)count / total * 100.0;
    }

    private static double ComputeWaterRatio(Mat hsvRegion)
    {
        // Ocean / Sea / Lake / Pool: Cyan/Deep-Blue/Teal (Hue in [65, 135], Sat >= 30, Val in [25, 175])
        // Distinguishes rich/darker body of water from bright/high-luminance sky
        using Mat waterMask = new();
        Scalar waterLow = new Scalar(65, 30, 25);
        Scalar waterHigh = new Scalar(135, 255, 175);
        Cv2.InRange(hsvRegion, waterLow, waterHigh, waterMask);

        int count = Cv2.CountNonZero(waterMask);
        int total = Math.Max(1, hsvRegion.Width * hsvRegion.Height);
        return (double)count / total * 100.0;
    }

    private static double ComputeVegetationRatio(Mat hsvRegion)
    {
        // Green foliage / grass: H in [30, 88], S >= 25, V >= 25
        using Mat vegMask = new();
        Scalar vegLow = new Scalar(30, 25, 25);
        Scalar vegHigh = new Scalar(88, 255, 255);
        Cv2.InRange(hsvRegion, vegLow, vegHigh, vegMask);

        int count = Cv2.CountNonZero(vegMask);
        int total = Math.Max(1, hsvRegion.Width * hsvRegion.Height);
        return (double)count / total * 100.0;
    }

    private static double ComputeTextureVariance(Mat grayRegion)
    {
        using Mat laplacian = new();
        Cv2.Laplacian(grayRegion, laplacian, MatType.CV_64F);

        Cv2.MeanStdDev(laplacian, out _, out Scalar stdDev);

        return Math.Min(100.0, stdDev.Val0);
    }
}
