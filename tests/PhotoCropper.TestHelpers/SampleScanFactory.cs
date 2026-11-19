using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System.Drawing;

namespace PhotoCropper.TestHelpers;

/// <summary>A generated scan and how many photos were placed on it.</summary>
public sealed record SampleScan(string Path, int PhotoCount);

/// <summary>
/// Generates scans that look closer to a real flatbed scan than the flat shapes of <see cref="TestImageFactory"/>:
/// an off-white bed with sensor noise, a slight lid shadow and dust, and one to four photos with gradients, shapes
/// and noise, each slightly tilted. The same seed always gives the same scan.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Test images: a seeded, repeatable generator is the point")]
public static class SampleScanFactory
{
    /// <summary>A4 at 150 dpi: big enough to look like a scan, small enough to process quickly.</summary>
    public const int DefaultWidth = 1240;
    public const int DefaultHeight = 1754;

    /// <summary>Writes a scan to <paramref name="path"/> (PNG or JPEG, by extension) and returns its photo count.</summary>
    /// <param name="photoCount">Photos to place (1 to 4); 0 picks one from the seed.</param>
    public static SampleScan Create(string path, int seed, int photoCount = 0, int width = DefaultWidth, int height = DefaultHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(photoCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(photoCount, 4);
        var random = new Random(seed);
        if (photoCount == 0) photoCount = random.Next(1, 5);

        using var scan = CreateBed(random, width, height);

        // A 2 x 2 grid of cells; each photo gets its own cell so photos never touch.
        int[] cells = [.. Enumerable.Range(0, 4).OrderBy(_ => random.Next())];
        int cellWidth = width / 2, cellHeight = height / 2;
        for (int i = 0; i < photoCount; i++)
        {
            int cell = cells[i];
            var cellRect = new Rectangle(cell % 2 * cellWidth, cell / 2 * cellHeight, cellWidth, cellHeight);
            PlacePhoto(scan, cellRect, random);
        }

        AddDust(scan, random);
        scan.Save(path);
        return new SampleScan(path, photoCount);
    }

    private static Mat CreateBed(Random random, int width, int height)
    {
        var bed = new Mat(height, width, DepthType.Cv8U, 3);
        double level = 238 + random.Next(0, 10);
        bed.SetTo(new MCvScalar(level, level, level - 2));

        // A faint shadow along one edge, as a scanner lid leaves.
        int shadow = height / 12;
        for (int y = 0; y < shadow; y++)
        {
            double darken = 14.0 * (1 - (double)y / shadow);
            using var row = new Mat(bed, new Rectangle(0, height - 1 - y, width, 1));
            using var amount = new ScalarArray(new MCvScalar(darken, darken, darken));
            CvInvoke.Subtract(row, amount, row);
        }

        AddNoise(bed, random, 3);
        return bed;
    }

    private static void PlacePhoto(Mat scan, Rectangle cell, Random random)
    {
        int photoWidth = (int)(cell.Width * (0.62 + random.NextDouble() * 0.16));
        int photoHeight = (int)(cell.Height * (0.55 + random.NextDouble() * 0.15));
        using var photo = CreatePhoto(random, photoWidth, photoHeight);

        double angle = random.NextDouble() * 10 - 5; // up to 5 degrees either way
        var center = new PointF(
            cell.X + cell.Width / 2f + (float)(random.NextDouble() * 20 - 10),
            cell.Y + cell.Height / 2f + (float)(random.NextDouble() * 20 - 10));

        // Rotate about the photo's centre, then move that centre to its place on the scan.
        using var transform = new Mat();
        CvInvoke.GetRotationMatrix2D(new PointF(photoWidth / 2f, photoHeight / 2f), angle, 1.0, transform);
        double[] m = new double[6];
        transform.CopyTo(m);
        m[2] += center.X - photoWidth / 2f;
        m[5] += center.Y - photoHeight / 2f;
        using var placed = new Mat(2, 3, DepthType.Cv64F, 1);
        placed.SetTo(m);

        using var layer = new Mat();
        using var mask = new Mat();
        using var fullMask = new Mat(photoHeight, photoWidth, DepthType.Cv8U, 1);
        fullMask.SetTo(new MCvScalar(255));
        CvInvoke.WarpAffine(photo, layer, placed, scan.Size, Inter.Linear, Warp.Default, BorderType.Constant);
        CvInvoke.WarpAffine(fullMask, mask, placed, scan.Size, Inter.Nearest, Warp.Default, BorderType.Constant);
        layer.CopyTo(scan, mask);
    }

    /// <summary>A photo-like image: a two-colour gradient, a few soft shapes, and grain.</summary>
    private static Mat CreatePhoto(Random random, int width, int height)
    {
        var photo = new Mat(height, width, DepthType.Cv8U, 3);
        MCvScalar top = RandomColour(random), bottom = RandomColour(random);
        for (int y = 0; y < height; y++)
        {
            double t = (double)y / Math.Max(1, height - 1);
            using var row = new Mat(photo, new Rectangle(0, y, width, 1));
            row.SetTo(new MCvScalar(
                top.V0 + (bottom.V0 - top.V0) * t,
                top.V1 + (bottom.V1 - top.V1) * t,
                top.V2 + (bottom.V2 - top.V2) * t));
        }

        int shapes = random.Next(3, 8);
        for (int i = 0; i < shapes; i++)
        {
            var centre = new Point(random.Next(width), random.Next(height));
            int radius = random.Next(Math.Max(4, width / 20), Math.Max(5, width / 5));
            CvInvoke.Circle(photo, centre, radius, RandomColour(random), -1, LineType.AntiAlias);
        }

        CvInvoke.GaussianBlur(photo, photo, new Size(0, 0), 2.5);
        AddNoise(photo, random, 6);
        return photo;
    }

    /// <summary>Mid-tone colours: clearly darker than the bed, so a photo's edge is always visible.</summary>
    private static MCvScalar RandomColour(Random random) =>
        new(random.Next(30, 190), random.Next(30, 190), random.Next(30, 190));

    private static void AddDust(Mat scan, Random random)
    {
        int specks = random.Next(4, 12);
        for (int i = 0; i < specks; i++)
        {
            var centre = new Point(random.Next(scan.Width), random.Next(scan.Height));
            double shade = random.Next(60, 140);
            CvInvoke.Circle(scan, centre, random.Next(1, 3), new MCvScalar(shade, shade, shade), -1, LineType.AntiAlias);
        }
    }

    /// <summary>Sensor-like grain, drawn from <paramref name="random"/> (not OpenCV's global generator) so a seed always gives the same image.</summary>
    private static void AddNoise(Mat image, Random random, int strength)
    {
        short[] values = new short[image.Rows * image.Cols * image.NumberOfChannels];
        for (int i = 0; i < values.Length; i++) values[i] = (short)random.Next(-strength, strength + 1);
        using var noise = new Mat(image.Size, DepthType.Cv16S, image.NumberOfChannels);
        noise.SetTo(values);
        using var wide = new Mat();
        image.ConvertTo(wide, DepthType.Cv16S);
        CvInvoke.Add(wide, noise, wide);
        wide.ConvertTo(image, DepthType.Cv8U); // saturates back into 0..255
    }
}
