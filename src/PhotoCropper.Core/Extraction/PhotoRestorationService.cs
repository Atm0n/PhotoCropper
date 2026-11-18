using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System.Drawing;

namespace PhotoCropper.Core.Extraction;

public static class PhotoRestorationService
{
    /// <summary>
    /// Restores faded vintage photographs by neutralizing aging color casts (auto-white balance / auto-levels),
    /// recovering shadow/highlight contrast via LAB CLAHE, and gently reviving bleached color saturation.
    /// Optionally detects and inpaints dust specks and hairline scratches.
    /// </summary>
    public static Mat RestoreColors(Mat photo, bool removeDust = false) =>
        Restore(photo, new Workspace.RestorationSettings { RestoreColors = true, RemoveDust = removeDust });

    /// <summary>
    /// Applies <paramref name="settings"/>: automatic restoration (white balance, contrast, saturation, each optional,
    /// scaled by strength), black-and-white handling, manual adjustments, then dust removal.
    /// Returns <paramref name="photo"/> itself when the settings change nothing.
    /// </summary>
    public static Mat Restore(Mat photo, Workspace.RestorationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(settings);
        if (photo.IsEmpty || photo.Width < 20 || photo.Height < 20 || !settings.ChangesPhoto) return photo;

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

        // Normal strength reproduces the original fixed pipeline exactly; Gentle/Strong scale every automatic step.
        double factor = settings.Strength switch
        {
            Workspace.RestorationStrength.Gentle => 0.5,
            Workspace.RestorationStrength.Strong => 1.5,
            _ => 1.0
        };

        bool isMonochrome = settings.ColorMode switch
        {
            Workspace.PhotoColorMode.Color => false,
            Workspace.PhotoColorMode.Auto => IsMonochrome(bgr),
            _ => true
        };

        Mat finalBgr = bgr.Clone();
        try
        {
            if (settings.RestoreColors)
            {
                // 1. Colour cast removal. Skipped for black-and-white prints, where it would strip a deliberate tone.
                if (settings.AutoWhiteBalance && !isMonochrome)
                {
                    Replace(ref finalBgr, ApplySoftWhiteBalance(finalBgr, damping: Math.Min(1.0, 0.55 * factor)));
                }

                // 2-3. Levels stretch, then local contrast recovery (CLAHE on the LAB L-channel only).
                if (settings.AutoContrast)
                {
                    double clip = 0.005 * factor;
                    Replace(ref finalBgr, ApplyUnifiedContrastStretch(finalBgr, lowPercentile: clip, highPercentile: 1.0 - clip));
                    Replace(ref finalBgr, ApplyLocalContrast(finalBgr, clipLimit: 1.0 + 0.3 * factor));
                }

                // 4. Gentle vibrancy revival of faded pigments (protects skin tones). Not for black and white.
                if (settings.AutoSaturation && !isMonochrome)
                {
                    Replace(ref finalBgr, ApplyVibrancy(finalBgr, (float)(1.0 + 0.10 * factor)));
                }
            }

            // 5. Black and white made neutral: removes any residual tint (e.g. yellowed paper).
            if (settings.ColorMode == Workspace.PhotoColorMode.BlackAndWhiteNeutral)
            {
                Replace(ref finalBgr, ToNeutralGray(finalBgr));
            }

            // 6. Manual adjustments, on top of the automatic result.
            if (settings.HasManualAdjustments)
            {
                Replace(ref finalBgr, ApplyManualAdjustments(finalBgr, settings));
            }

            // 7. Automated dust, hair and scratch inpainting.
            if (settings.RemoveDust)
            {
                Replace(ref finalBgr, InpaintDustAndScratches(finalBgr));
            }
        }
        catch
        {
            finalBgr.Dispose();
            throw;
        }

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

    /// <summary>
    /// True when a candidate defect stands on its own (dust or a speck on clean background) rather than being part
    /// of natural structure. A speck's own outline always produces edges, so only edges in a small ring
    /// <em>around</em> it count: many of them mean a contour or detail runs through, and the candidate is kept.
    /// </summary>
    private static bool IsIsolatedDefect(Emgu.CV.Util.VectorOfVectorOfPoint contours, int index, Rectangle bounds, Mat edges)
    {
        const int ring = 4;
        Rectangle area = Rectangle.Inflate(bounds, ring, ring);
        area.Intersect(new Rectangle(0, 0, edges.Width, edges.Height));
        if (area.Width <= 0 || area.Height <= 0) return false;

        // The defect itself, grown by 2 px so its own outline edges fall inside it.
        using Mat own = Mat.Zeros(area.Height, area.Width, DepthType.Cv8U, 1);
        CvInvoke.DrawContours(own, contours, index, new MCvScalar(255), -1, LineType.EightConnected, null, int.MaxValue, new Point(-area.X, -area.Y));
        using Mat grow = CvInvoke.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5), new Point(-1, -1));
        CvInvoke.Dilate(own, own, grow, new Point(-1, -1), 1, BorderType.Constant, new MCvScalar());

        using Mat edgeArea = new(edges, area);
        using Mat foreignEdges = new();
        CvInvoke.Subtract(edgeArea, own, foreignEdges);

        // Dust on clean background has no edges of its own in the ring at all; a corner or contour passing by does.
        // Being strict here errs on the side of leaving a speck rather than smearing real detail.
        const int maxForeignEdgePixels = 2;
        return CvInvoke.CountNonZero(foreignEdges) <= maxForeignEdgePixels;
    }

    /// <summary>Disposes <paramref name="current"/> and replaces it with <paramref name="next"/>.</summary>
    private static void Replace(ref Mat current, Mat next)
    {
        if (ReferenceEquals(current, next)) return;
        current.Dispose();
        current = next;
    }

    /// <summary>
    /// True for black-and-white prints, including toned ones (sepia, selenium): nearly every pixel shares the same
    /// tint. Colour photos, even faded ones, have pixels clearly off that common tint.
    /// </summary>
    internal static bool IsMonochrome(Mat bgr)
    {
        ArgumentNullException.ThrowIfNull(bgr);
        if (bgr.IsEmpty) return false;
        if (bgr.NumberOfChannels == 1) return true;

        // A small copy is enough to judge colour content.
        using Mat small = new();
        double scale = Math.Min(1.0, 256.0 / Math.Max(bgr.Width, bgr.Height));
        CvInvoke.Resize(bgr, small, new Size(Math.Max(1, (int)(bgr.Width * scale)), Math.Max(1, (int)(bgr.Height * scale))), 0, 0, Inter.Area);

        using Mat lab = new();
        CvInvoke.CvtColor(small, lab, ColorConversion.Bgr2Lab);
        using var channels = new Emgu.CV.Util.VectorOfMat();
        CvInvoke.Split(lab, channels);

        using Mat a = new();
        using Mat b = new();
        channels[1].ConvertTo(a, DepthType.Cv32F);
        channels[2].ConvertTo(b, DepthType.Cv32F);

        // Distance of each pixel's chroma from the photo's average tint.
        double meanA = CvInvoke.Mean(a).V0;
        double meanB = CvInvoke.Mean(b).V0;
        using Mat da = new();
        using Mat db = new();
        using var meanAArray = new ScalarArray(meanA);
        using var meanBArray = new ScalarArray(meanB);
        CvInvoke.Subtract(a, meanAArray, da);
        CvInvoke.Subtract(b, meanBArray, db);
        using Mat distance = new();
        using Mat angle = new();
        CvInvoke.CartToPolar(da, db, distance, angle);

        // Black and white if fewer than 2% of pixels stray noticeably from the common tint.
        using Mat offTint = new();
        CvInvoke.Threshold(distance, offTint, 8.0, 1.0, ThresholdType.Binary);
        double offTintFraction = CvInvoke.Mean(offTint).V0;
        return offTintFraction < 0.02;
    }

    /// <summary>Local contrast recovery: CLAHE on the LAB lightness channel, leaving colour untouched.</summary>
    private static Mat ApplyLocalContrast(Mat bgr, double clipLimit)
    {
        using Mat lab = new();
        CvInvoke.CvtColor(bgr, lab, ColorConversion.Bgr2Lab);
        using var labChannels = new Emgu.CV.Util.VectorOfMat();
        CvInvoke.Split(lab, labChannels);

        using Mat lEnhanced = new();
        CvInvoke.CLAHE(labChannels[0], clipLimit, new Size(8, 8), lEnhanced);

        using var merged = new Emgu.CV.Util.VectorOfMat(lEnhanced, labChannels[1], labChannels[2]);
        using Mat enhancedLab = new();
        CvInvoke.Merge(merged, enhancedLab);

        Mat result = new();
        CvInvoke.CvtColor(enhancedLab, result, ColorConversion.Lab2Bgr);
        return result;
    }

    /// <summary>Vibrancy: boosts low saturation more than high saturation (keeps skin tones natural).</summary>
    private static Mat ApplyVibrancy(Mat bgr, float factor)
    {
        using Mat hsv = new();
        CvInvoke.CvtColor(bgr, hsv, ColorConversion.Bgr2Hsv);
        using var hsvChannels = new Emgu.CV.Util.VectorOfMat();
        CvInvoke.Split(hsv, hsvChannels);

        using Mat satLut = CreateVibrancyLookupTable(factor);
        using Mat satEnhanced = new();
        CvInvoke.LUT(hsvChannels[1], satLut, satEnhanced);

        using var merged = new Emgu.CV.Util.VectorOfMat(hsvChannels[0], satEnhanced, hsvChannels[2]);
        using Mat finalHsv = new();
        CvInvoke.Merge(merged, finalHsv);

        Mat result = new();
        CvInvoke.CvtColor(finalHsv, result, ColorConversion.Hsv2Bgr);
        return result;
    }

    private static Mat ToNeutralGray(Mat bgr)
    {
        using Mat gray = new();
        CvInvoke.CvtColor(bgr, gray, ColorConversion.Bgr2Gray);
        Mat result = new();
        CvInvoke.CvtColor(gray, result, ColorConversion.Gray2Bgr);
        return result;
    }

    /// <summary>
    /// Manual adjustments (each -100..100): brightness and contrast on all channels around mid-grey, warmth
    /// (blue ↔ yellow) and tint (green ↔ magenta) in LAB, and saturation in HSV.
    /// </summary>
    private static Mat ApplyManualAdjustments(Mat bgr, Workspace.RestorationSettings settings)
    {
        Mat current = bgr.Clone();
        try
        {
            if (settings.Brightness != 0 || settings.Contrast != 0)
            {
                double alpha = 1.0 + Math.Clamp(settings.Contrast, -100, 100) / 100.0 * 0.6;      // 0.4 .. 1.6
                double beta = 128.0 * (1.0 - alpha) + Math.Clamp(settings.Brightness, -100, 100) * 0.5; // ±50 levels
                Mat adjusted = new();
                current.ConvertTo(adjusted, DepthType.Cv8U, alpha, beta);
                Replace(ref current, adjusted);
            }

            if (settings.Warmth != 0 || settings.Tint != 0)
            {
                using Mat lab = new();
                CvInvoke.CvtColor(current, lab, ColorConversion.Bgr2Lab);
                using var channels = new Emgu.CV.Util.VectorOfMat();
                CvInvoke.Split(lab, channels);

                using Mat aShifted = new();
                using Mat bShifted = new();
                channels[1].ConvertTo(aShifted, DepthType.Cv8U, 1.0, Math.Clamp(settings.Tint, -100, 100) * 0.15);   // magenta +, green -
                channels[2].ConvertTo(bShifted, DepthType.Cv8U, 1.0, Math.Clamp(settings.Warmth, -100, 100) * 0.20); // yellow +, blue -

                using var merged = new Emgu.CV.Util.VectorOfMat(channels[0], aShifted, bShifted);
                using Mat shiftedLab = new();
                CvInvoke.Merge(merged, shiftedLab);
                Mat result = new();
                CvInvoke.CvtColor(shiftedLab, result, ColorConversion.Lab2Bgr);
                Replace(ref current, result);
            }

            if (settings.Saturation != 0 && settings.ColorMode != Workspace.PhotoColorMode.BlackAndWhiteNeutral)
            {
                using Mat hsv = new();
                CvInvoke.CvtColor(current, hsv, ColorConversion.Bgr2Hsv);
                using var channels = new Emgu.CV.Util.VectorOfMat();
                CvInvoke.Split(hsv, channels);

                using Mat sScaled = new();
                channels[1].ConvertTo(sScaled, DepthType.Cv8U, 1.0 + Math.Clamp(settings.Saturation, -100, 100) / 100.0, 0);

                using var merged = new Emgu.CV.Util.VectorOfMat(channels[0], sScaled, channels[2]);
                using Mat scaledHsv = new();
                CvInvoke.Merge(merged, scaledHsv);
                Mat result = new();
                CvInvoke.CvtColor(scaledHsv, result, ColorConversion.Hsv2Bgr);
                Replace(ref current, result);
            }

            return current;
        }
        catch
        {
            current.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Detects high-frequency hairline scratches, dust specks, and fibers using morphological
    /// Black-Hat (dark defects) and Top-Hat (bright defects) filtering, then in-paints them.
    /// </summary>
    public static Mat InpaintDustAndScratches(Mat bgr, double inpaintRadius = 2.5)
    {
        ArgumentNullException.ThrowIfNull(bgr);
        if (bgr.IsEmpty || bgr.Width < 30 || bgr.Height < 30) return bgr.Clone();

        using Mat gray = new();
        CvInvoke.CvtColor(bgr, gray, ColorConversion.Bgr2Gray);

        // Median blur removes fine natural film grain so we only target true dust/scratches
        using Mat smooth = new();
        CvInvoke.MedianBlur(gray, smooth, 3);

        // 3x3 to 5x5 elliptical structuring element for isolated dust & thin lines
        // Black-hat/top-hat only find features smaller than the kernel, so it grows with resolution: dust that is
        // 2 px on a small photo is 8 px on a 600 DPI scan. About 1/200 of the shorter side, odd, 5..15 px.
        int kernelSize = Math.Clamp(Math.Min(bgr.Width, bgr.Height) / 200, 5, 15) | 1;
        using Mat kernel = CvInvoke.GetStructuringElement(MorphShapes.Ellipse, new Size(kernelSize, kernelSize), new Point(-1, -1));

        using Mat blackHat = new();
        CvInvoke.MorphologyEx(smooth, blackHat, MorphOp.Blackhat, kernel, new Point(-1, -1), 1, BorderType.Reflect, new MCvScalar());

        using Mat topHat = new();
        CvInvoke.MorphologyEx(smooth, topHat, MorphOp.Tophat, kernel, new Point(-1, -1), 1, BorderType.Reflect, new MCvScalar());

        using Mat threshBlack = new();
        using Mat threshTop = new();
        CvInvoke.Threshold(blackHat, threshBlack, 28, 255, ThresholdType.Binary);
        CvInvoke.Threshold(topHat, threshTop, 35, 255, ThresholdType.Binary);

        using Mat defectMask = new();
        CvInvoke.BitwiseOr(threshBlack, threshTop, defectMask);

        // Edges, used to protect natural detail (eyes, contours, text) from being inpainted.
        using Mat edges = new();
        CvInvoke.Canny(smooth, edges, 60, 160);

        using var contours = new Emgu.CV.Util.VectorOfVectorOfPoint();
        using Mat hierarchy = new();
        CvInvoke.FindContours(defectMask, contours, hierarchy, RetrType.External, ChainApproxMethod.ChainApproxSimple);

        using Mat filteredMask = Mat.Zeros(defectMask.Rows, defectMask.Cols, DepthType.Cv8U, 1);
        int maxDefectArea = Math.Max(25, (int)(bgr.Width * bgr.Height * 0.0008)); // max 0.08% of photo area

        for (int i = 0; i < contours.Size; i++)
        {
            using var contour = contours[i];
            Rectangle bounds = CvInvoke.BoundingRectangle(contour);

            // Ignore large blobs: those are image features, not dust.
            if (bounds.Width * bounds.Height > maxDefectArea * 2) continue;

            if (IsIsolatedDefect(contours, i, bounds, edges))
            {
                CvInvoke.DrawContours(filteredMask, contours, i, new MCvScalar(255), -1);
            }
        }

        // Slightly dilate the defects to cover boundary penumbra
        using Mat finalMask = new();
        using Mat dilateKernel = CvInvoke.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3), new Point(-1, -1));
        CvInvoke.Dilate(filteredMask, finalMask, dilateKernel, new Point(-1, -1), 1, BorderType.Reflect, new MCvScalar());

        // Inpaint defects using Fast Marching Method (Telea)
        Mat result = new();
        CvInvoke.Inpaint(bgr, finalMask, result, inpaintRadius, InpaintType.Telea);
        return result;
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

    /// <summary>
    /// Levels stretch on lightness only (LAB L channel). Applying one curve to all three colour channels instead
    /// would multiply the differences between them by the stretch factor, amplifying colour casts (a yellowed,
    /// low-contrast print came out more yellow) and clipping the weakest channel to black.
    /// </summary>
    private static Mat ApplyUnifiedContrastStretch(Mat bgr, double lowPercentile, double highPercentile)
    {
        using Mat lab = new();
        CvInvoke.CvtColor(bgr, lab, ColorConversion.Bgr2Lab);
        using var labChannels = new Emgu.CV.Util.VectorOfMat();
        CvInvoke.Split(lab, labChannels);
        using Mat gray = labChannels[0].Clone();

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

        // Stretch lightness only; the colour channels (a, b) are kept, so hues and casts are not amplified.
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

        using Mat stretchedL = new();
        CvInvoke.LUT(gray, lut, stretchedL);

        using var merged = new Emgu.CV.Util.VectorOfMat(stretchedL, labChannels[1], labChannels[2]);
        using Mat stretchedLab = new();
        CvInvoke.Merge(merged, stretchedLab);

        Mat destination = new();
        CvInvoke.CvtColor(stretchedLab, destination, ColorConversion.Lab2Bgr);
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
