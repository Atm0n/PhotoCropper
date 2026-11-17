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

            using Mat repairedBgr = new();
            CvInvoke.Inpaint(bgr, mask, repairedBgr, inpaintRadius, InpaintType.Telea);

            using var outChannels = new Emgu.CV.Util.VectorOfMat();
            CvInvoke.Split(repairedBgr, outChannels);
            using var withAlpha = new Emgu.CV.Util.VectorOfMat(outChannels[0], outChannels[1], outChannels[2], channels[3]);
            Mat result = new();
            CvInvoke.Merge(withAlpha, result);
            return result;
        }

        Mat repaired = new();
        CvInvoke.Inpaint(image, mask, repaired, inpaintRadius, InpaintType.Telea);
        return repaired;
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

    private static Point ToPixel(RepairStroke stroke, int index, Size size) => new(
        (int)Math.Round(Math.Clamp(stroke.Points[index * 2], 0f, 1f) * (size.Width - 1)),
        (int)Math.Round(Math.Clamp(stroke.Points[index * 2 + 1], 0f, 1f) * (size.Height - 1)));
}
