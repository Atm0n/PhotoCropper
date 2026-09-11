using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System.Drawing;

namespace PhotoCropper.Extraction;

public static class PhotoRestorationService
{
    /// <summary>
    /// Restores faded vintage photographs by neutralizing aging color casts (auto-white balance / auto-levels),
    /// recovering shadow/highlight contrast via LAB CLAHE, and gently reviving bleached color saturation.
    /// </summary>
    public static Mat RestoreColors(Mat photo)
    {
        ArgumentNullException.ThrowIfNull(photo);
        if (photo.IsEmpty || photo.Width < 20 || photo.Height < 20) return photo;

        bool hasAlpha = photo.NumberOfChannels == 4;
        using Mat bgr = new();
        using Mat alpha = new();

        if (hasAlpha)
        {
            using var channels = new Emgu.CV.Util.VectorOfMat();
            CvInvoke.Split(photo, channels);
            // channels[0] = B, channels[1] = G, channels[2] = R, channels[3] = A
            channels[3].CopyTo(alpha);

            using var bgrVec = new Emgu.CV.Util.VectorOfMat(channels[0], channels[1], channels[2]);
            CvInvoke.Merge(bgrVec, bgr);
        }
        else if (photo.NumberOfChannels == 1)
        {
            CvInvoke.CvtColor(photo, bgr, ColorConversion.Gray2Bgr);
        }
        else
        {
            photo.CopyTo(bgr);
        }

        // 1. Soft White Balance & Color Cast Removal (Preserves true hue without introducing blue/yellow tint)
        using Mat balanced = ApplySoftWhiteBalance(bgr, damping: 0.55);

        // 2. Master Dynamic Range Stretching (Unified Luminance auto-levels to prevent color shift)
        using Mat contrastStretched = ApplyUnifiedContrastStretch(balanced, lowPercentile: 0.005, highPercentile: 0.995);

        // 3. Adaptive Contrast Recovery in LAB Color Space (CLAHE on L-channel only)
        using Mat lab = new();
        CvInvoke.CvtColor(contrastStretched, lab, ColorConversion.Bgr2Lab);

        using var labChannels = new Emgu.CV.Util.VectorOfMat();
        CvInvoke.Split(lab, labChannels);

        using Mat lEnhanced = new();
        CvInvoke.CLAHE(labChannels[0], 1.3, new Size(8, 8), 1, lEnhanced);

        using var mergedLabVec = new Emgu.CV.Util.VectorOfMat(lEnhanced, labChannels[1], labChannels[2]);
        using Mat enhancedLab = new();
        CvInvoke.Merge(mergedLabVec, enhancedLab);

        using Mat enhancedBgr = new();
        CvInvoke.CvtColor(enhancedLab, enhancedBgr, ColorConversion.Lab2Bgr);

        // 4. Gentle Vibrancy Revival in HSV (boost faded pigments by ~10% while protecting skin tones)
        using Mat hsv = new();
        CvInvoke.CvtColor(enhancedBgr, hsv, ColorConversion.Bgr2Hsv);

        using var hsvChannels = new Emgu.CV.Util.VectorOfMat();
        CvInvoke.Split(hsv, hsvChannels);

        using Mat satLut = CreateVibrancyLookupTable(1.10f);
        using Mat satEnhanced = new();
        CvInvoke.LUT(hsvChannels[1], satLut, satEnhanced);

        using var mergedHsvVec = new Emgu.CV.Util.VectorOfMat(hsvChannels[0], satEnhanced, hsvChannels[2]);
        using Mat finalHsv = new();
        CvInvoke.Merge(mergedHsvVec, finalHsv);

        Mat finalBgr = new();
        CvInvoke.CvtColor(finalHsv, finalBgr, ColorConversion.Hsv2Bgr);

        if (hasAlpha && !alpha.IsEmpty)
        {
            using var outChannels = new Emgu.CV.Util.VectorOfMat();
            CvInvoke.Split(finalBgr, outChannels);

            using var outWithAlpha = new Emgu.CV.Util.VectorOfMat(outChannels[0], outChannels[1], outChannels[2], alpha);
            Mat bgraResult = new();
            CvInvoke.Merge(outWithAlpha, bgraResult);
            finalBgr.Dispose();
            return bgraResult;
        }

        return finalBgr;
    }

    private static Mat ApplySoftWhiteBalance(Mat bgr, double damping)
    {
        using var channels = new Emgu.CV.Util.VectorOfMat();
        CvInvoke.Split(bgr, channels);

        double meanB = CvInvoke.Mean(channels[0]).V0;
        double meanG = CvInvoke.Mean(channels[1]).V0;
        double meanR = CvInvoke.Mean(channels[2]).V0;
        double grayAvg = (meanB + meanG + meanR) / 3.0;

        if (meanB < 1.0 || meanG < 1.0 || meanR < 1.0) return bgr.Clone();

        // Calculate damped channel gains clamped to a safe band ([0.85, 1.18]) to avoid blue/yellow color drift
        double rawGainB = grayAvg / meanB;
        double rawGainG = grayAvg / meanG;
        double rawGainR = grayAvg / meanR;

        double gainB = Math.Clamp(1.0 + (rawGainB - 1.0) * damping, 0.85, 1.18);
        double gainG = Math.Clamp(1.0 + (rawGainG - 1.0) * damping, 0.88, 1.15);
        double gainR = Math.Clamp(1.0 + (rawGainR - 1.0) * damping, 0.85, 1.18);

        using Mat bScaled = new();
        using Mat gScaled = new();
        using Mat rScaled = new();

        channels[0].ConvertTo(bScaled, DepthType.Cv8U, gainB, 0);
        channels[1].ConvertTo(gScaled, DepthType.Cv8U, gainG, 0);
        channels[2].ConvertTo(rScaled, DepthType.Cv8U, gainR, 0);

        using var scaledVec = new Emgu.CV.Util.VectorOfMat(bScaled, gScaled, rScaled);
        Mat result = new();
        CvInvoke.Merge(scaledVec, result);
        return result;
    }

    private static Mat ApplyUnifiedContrastStretch(Mat bgr, double lowPercentile, double highPercentile)
    {
        using Mat gray = new();
        CvInvoke.CvtColor(bgr, gray, ColorConversion.Bgr2Gray);

        using Mat hist = new();
        using var vec = new Emgu.CV.Util.VectorOfMat(gray);
        CvInvoke.CalcHist(vec, [0], null, hist, [256], [0, 256], false);

        float[] histData = new float[256];
        hist.CopyTo(histData);

        int totalPixels = gray.Width * gray.Height;
        int lowThreshold = (int)Math.Round(totalPixels * lowPercentile);
        int highThreshold = (int)Math.Round(totalPixels * highPercentile);

        int minVal = 0;
        int maxVal = 255;
        int cumulative = 0;

        for (int i = 0; i < 256; i++)
        {
            cumulative += (int)histData[i];
            if (cumulative >= lowThreshold)
            {
                minVal = i;
                break;
            }
        }

        cumulative = 0;
        for (int i = 255; i >= 0; i--)
        {
            cumulative += (int)histData[i];
            if (cumulative >= (totalPixels - highThreshold))
            {
                maxVal = i;
                break;
            }
        }

        if (maxVal <= minVal || (minVal <= 2 && maxVal >= 253))
        {
            return bgr.Clone();
        }

        // Apply identical luminance-derived LUT across all 3 color channels to preserve exact hues
        byte[] lutBytes = new byte[256];
        double range = maxVal - minVal;
        for (int i = 0; i < 256; i++)
        {
            if (i <= minVal) lutBytes[i] = 0;
            else if (i >= maxVal) lutBytes[i] = 255;
            else lutBytes[i] = (byte)Math.Clamp(Math.Round((i - minVal) * 255.0 / range), 0, 255);
        }

        using Mat lut = new(1, 256, DepthType.Cv8U, 1);
        lut.SetTo(lutBytes);

        Mat destination = new();
        CvInvoke.LUT(bgr, lut, destination);
        return destination;
    }

    private static Mat CreateVibrancyLookupTable(float factor)
    {
        byte[] lutBytes = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            // Subtle curve: boost midtones more than saturated highlights
            double val = i * (1.0 + (factor - 1.0) * (1.0 - Math.Pow(i / 255.0, 2)));
            lutBytes[i] = (byte)Math.Clamp(Math.Round(val), 0, 255);
        }

        Mat lut = new(1, 256, DepthType.Cv8U, 1);
        lut.SetTo(lutBytes);
        return lut;
    }
}
