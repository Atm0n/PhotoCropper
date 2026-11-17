using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Core.Workspace;
using System.Drawing;

namespace PhotoCropper.Core.Extraction;

/// <summary>
/// Repair brush: fills in the areas painted by the user (scratches, creases, stains) from their surroundings.
/// </summary>
public static class PhotoRepairService
{
    /// <summary>Builds the repair mask (255 where painted) for an image of <paramref name="size"/>.</summary>
    public static Mat CreateMask(Size size, IEnumerable<RepairStroke> strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        Mat mask = Mat.Zeros(size.Height, size.Width, DepthType.Cv8U, 1);
        var white = new MCvScalar(255);

        foreach (var stroke in strokes)
        {
            if (stroke.PointCount == 0) continue;
            int radius = Math.Max(1, (int)Math.Round(stroke.Radius * size.Width));

            Point previous = ToPixel(stroke, 0, size);
            CvInvoke.Circle(mask, previous, radius, white, -1, LineType.AntiAlias);
            for (int i = 1; i < stroke.PointCount; i++)
            {
                Point next = ToPixel(stroke, i, size);
                CvInvoke.Line(mask, previous, next, white, radius * 2, LineType.AntiAlias);
                CvInvoke.Circle(mask, next, radius, white, -1, LineType.AntiAlias);
                previous = next;
            }
        }

        // Anti-aliased edges are partial; anything touched is repaired.
        CvInvoke.Threshold(mask, mask, 0, 255, ThresholdType.Binary);
        return mask;
    }

    /// <summary>
    /// Fills in the painted areas from their surroundings. Returns <paramref name="image"/> itself when there is
    /// nothing to repair. Transparency (alpha) is kept as it was.
    /// </summary>
    public static Mat ApplyRepairs(Mat image, IReadOnlyCollection<RepairStroke> strokes)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(strokes);
        if (image.IsEmpty || strokes.Count == 0) return image;

        using Mat mask = CreateMask(image.Size, strokes);
        if (CvInvoke.CountNonZero(mask) == 0) return image;

        // Larger brushes need a wider neighbourhood to fill from.
        double widestRadius = strokes.Max(s => s.Radius) * image.Width;
        double inpaintRadius = Math.Clamp(widestRadius, 3, 25);

        if (image.NumberOfChannels == 4)
        {
            using var channels = new Emgu.CV.Util.VectorOfMat();
            CvInvoke.Split(image, channels);
            using var bgrVec = new Emgu.CV.Util.VectorOfMat(channels[0], channels[1], channels[2]);
            using Mat bgr = new();
            CvInvoke.Merge(bgrVec, bgr);

            using Mat repairedBgr = FillWithGrain(bgr, mask, inpaintRadius);

            using var outChannels = new Emgu.CV.Util.VectorOfMat();
            CvInvoke.Split(repairedBgr, outChannels);
            using var withAlpha = new Emgu.CV.Util.VectorOfMat(outChannels[0], outChannels[1], outChannels[2], channels[3]);
            Mat result = new();
            CvInvoke.Merge(withAlpha, result);
            return result;
        }

        return FillWithGrain(image, mask, inpaintRadius);
    }

    /// <summary>
    /// The photo's repairs if they fit <paramref name="image"/> (painted on a cropped photo of the same size);
    /// otherwise none, since strokes from an earlier crop would land in the wrong place.
    /// </summary>
    public static IReadOnlyCollection<RepairStroke> ApplicableRepairs(ProjectPhoto photo, Mat image)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(image);
        return photo.Repairs.Count == 0 || photo.AreRepairsOutdated(image.Width, image.Height) ? [] : photo.Repairs;
    }

    /// <summary>
    /// Telea fill plus grain. Telea blends colours inward, which leaves a smooth, blurry-looking patch on grainy
    /// photos. The fine detail (grain) is measured in a ring around the repair and the same amount is added back
    /// inside it, with soft edges, so the repair blends in. A fixed seed makes the result reproducible.
    /// Works on 1- or 3-channel 8-bit images.
    /// </summary>
    private static Mat FillWithGrain(Mat bgr, Mat mask, double inpaintRadius)
    {
        Mat filled = new();
        CvInvoke.Inpaint(bgr, mask, filled, inpaintRadius, InpaintType.Telea);
        try
        {
            // Grain = the image minus a slightly blurred copy, measured in a ring just outside the repair.
            int ringWidth = (int)Math.Max(6, inpaintRadius * 2);
            using Mat ring = new();
            using (Mat grow = CvInvoke.GetStructuringElement(MorphShapes.Ellipse, new Size(ringWidth * 2 + 1, ringWidth * 2 + 1), new Point(-1, -1)))
            {
                CvInvoke.Dilate(mask, ring, grow, new Point(-1, -1), 1, BorderType.Constant, new MCvScalar());
            }
            CvInvoke.Subtract(ring, mask, ring);
            if (CvInvoke.CountNonZero(ring) < 20) return filled;

            using Mat source = new();
            bgr.ConvertTo(source, DepthType.Cv32F);
            using Mat smooth = new();
            CvInvoke.GaussianBlur(source, smooth, new Size(0, 0), 1.5);
            using Mat grain = new();
            CvInvoke.Subtract(source, smooth, grain);

            var mean = new MCvScalar();
            var grainStd = new MCvScalar();
            CvInvoke.MeanStdDev(grain, ref mean, ref grainStd, ring);
            int channelCount = bgr.NumberOfChannels;
            double[] strengths = [grainStd.V0, grainStd.V1, grainStd.V2, grainStd.V3];
            if (strengths.Take(channelCount).Average() < 0.5) return filled; // smooth photo: nothing to add

            // One noise pattern for all channels (film grain is mostly in brightness), scaled per channel.
            using Mat shapedNoise = CreateNoise(bgr.Size, mask);

            // Soft-edged weight so the added grain fades in at the boundary of the repair.
            using Mat weight = new();
            mask.ConvertTo(weight, DepthType.Cv32F, 1.0 / 255.0);
            CvInvoke.GaussianBlur(weight, weight, new Size(0, 0), 1.0);
            using Mat weightedNoise = new();
            CvInvoke.Multiply(shapedNoise, weight, weightedNoise);

            var perChannel = new List<Mat>(channelCount);
            using var noiseChannels = new Emgu.CV.Util.VectorOfMat();
            try
            {
                for (int c = 0; c < channelCount; c++)
                {
                    var channelNoise = new Mat();
                    perChannel.Add(channelNoise);
                    weightedNoise.ConvertTo(channelNoise, DepthType.Cv32F, strengths[c]);
                    noiseChannels.Push(channelNoise);
                }

                using Mat noiseAll = new();
                CvInvoke.Merge(noiseChannels, noiseAll);

                using Mat filledFloat = new();
                filled.ConvertTo(filledFloat, DepthType.Cv32F);
                CvInvoke.Add(filledFloat, noiseAll, filledFloat);

                Mat result = new();
                filledFloat.ConvertTo(result, DepthType.Cv8U);
                filled.Dispose();
                return result;
            }
            finally
            {
                foreach (var m in perChannel) m.Dispose();
            }
        }
        catch
        {
            filled.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Grain-like noise with a standard deviation of 1, only around the masked area (zero elsewhere): Gaussian noise,
    /// slightly blurred like real grain, then normalised within that area. Uses a fixed seed so the same repair
    /// always gets the same grain.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Visual film grain, not security: a fixed, reproducible seed is the point")]
    private static Mat CreateNoise(Size size, Mat mask)
    {
        Mat noise = Mat.Zeros(size.Height, size.Width, DepthType.Cv32F, 1);
        using Mat points = new();
        CvInvoke.FindNonZero(mask, points);
        if (points.IsEmpty) return noise;

        Rectangle area = Rectangle.Inflate(CvInvoke.BoundingRectangle(points), 4, 4);
        area.Intersect(new Rectangle(Point.Empty, size));

        var random = new Random(20241117);
        var values = new float[area.Width * area.Height];
        for (int i = 0; i < values.Length; i++)
        {
            // Box-Muller transform: two uniform samples → one standard normal sample.
            double u1 = 1.0 - random.NextDouble();
            double u2 = random.NextDouble();
            values[i] = (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        using Mat raw = new(area.Height, area.Width, DepthType.Cv32F, 1);
        raw.SetTo(values);
        using Mat shaped = new();
        CvInvoke.GaussianBlur(raw, shaped, new Size(0, 0), 0.6);

        // Blurring lowers the strength; scale back to exactly 1 within this area.
        var mean = new MCvScalar();
        var std = new MCvScalar();
        CvInvoke.MeanStdDev(shaped, ref mean, ref std);
        using Mat region = new(noise, area);
        shaped.ConvertTo(region, DepthType.Cv32F, std.V0 > 0 ? 1.0 / std.V0 : 1.0, std.V0 > 0 ? -mean.V0 / std.V0 : 0);
        return noise;
    }

    private static Point ToPixel(RepairStroke stroke, int index, Size size) => new(
        (int)Math.Round(Math.Clamp(stroke.Points[index * 2], 0f, 1f) * (size.Width - 1)),
        (int)Math.Round(Math.Clamp(stroke.Points[index * 2 + 1], 0f, 1f) * (size.Height - 1)));
}
