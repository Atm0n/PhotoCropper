using OpenCvSharp;
using PhotoCropper.Core.Common;

namespace PhotoCropper.TestHelpers;

/// <summary>A generated scan and how many photos were placed on it.</summary>
public sealed record SampleScan(string Path, int PhotoCount);

/// <summary>A generated scan and where its photos are, in scan pixels.</summary>
public sealed record AlbumScan(string Path, IReadOnlyList<Rect> Photos);

/// <summary>
/// Generates scans that look closer to a real flatbed scan than the flat shapes of <see cref="TestImageFactory"/>:
/// an off-white bed with sensor noise, a slight lid shadow and dust, and one to four photos with gradients, shapes
/// and noise, each slightly tilted. The same seed always gives the same scan.
/// </summary>
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
        var random = new SeededRandom(seed);
        if (photoCount == 0) photoCount = random.Next(1, 5);

        using var scan = CreateBed(random, width, height);

        // A 2 x 2 grid of cells; each photo gets its own cell so photos never touch.
        int[] cells = [.. Enumerable.Range(0, 4).OrderBy(_ => random.Next())];
        int cellWidth = width / 2, cellHeight = height / 2;
        for (int i = 0; i < photoCount; i++)
        {
            int cell = cells[i];
            var cellRect = new Rect(cell % 2 * cellWidth, cell / 2 * cellHeight, cellWidth, cellHeight);
            PlacePhoto(scan, cellRect, random);
        }

        AddDust(scan, random);
        Cv2.ImWrite(path, scan);
        return new SampleScan(path, photoCount);
    }

    private static Mat CreateBed(SeededRandom random, int width, int height)
    {
        var bed = new Mat(height, width, MatType.CV_8UC3);
        double level = 238 + random.Next(0, 10);
        bed.SetTo(new Scalar(level, level, level - 2));

        // A faint shadow along one edge, as a scanner lid leaves.
        int shadow = height / 12;
        for (int y = 0; y < shadow; y++)
        {
            double darken = 14.0 * (1 - (double)y / shadow);
            using var row = new Mat(bed, new Rect(0, height - 1 - y, width, 1));
            var amount = new Scalar(darken, darken, darken);
            Cv2.Subtract(row, amount, row);
        }

        AddNoise(bed, random, 3);
        return bed;
    }

    private static void PlacePhoto(Mat scan, Rect cell, SeededRandom random)
    {
        int photoWidth = (int)(cell.Width * (0.62 + random.NextDouble() * 0.16));
        int photoHeight = (int)(cell.Height * (0.55 + random.NextDouble() * 0.15));
        using var photo = CreatePhoto(random, photoWidth, photoHeight);

        double angle = random.NextDouble() * 10 - 5; // up to 5 degrees either way
        var center = new Point2f(
            cell.X + cell.Width / 2f + (float)(random.NextDouble() * 20 - 10),
            cell.Y + cell.Height / 2f + (float)(random.NextDouble() * 20 - 10));

        // Rotate about the photo's centre, then move that centre to its place on the scan.
        using var transform = Cv2.GetRotationMatrix2D(new Point2f(photoWidth / 2f, photoHeight / 2f), angle, 1.0);
        double[] m = new double[6];
        transform.CopyDataTo(m);
        m[2] += center.X - photoWidth / 2f;
        m[5] += center.Y - photoHeight / 2f;
        using var placed = new Mat(2, 3, MatType.CV_64FC1);
        placed.SetData(m);

        using var layer = new Mat();
        using var mask = new Mat();
        using var fullMask = new Mat(photoHeight, photoWidth, MatType.CV_8UC1);
        fullMask.SetTo(new Scalar(255));
        Cv2.WarpAffine(photo, layer, placed, scan.Size(), InterpolationFlags.Linear, BorderTypes.Constant);
        Cv2.WarpAffine(fullMask, mask, placed, scan.Size(), InterpolationFlags.Nearest, BorderTypes.Constant);
        layer.CopyTo(scan, mask);
    }

    /// <summary>Salmon album paper (BGR), as on old album pages photos were stuck to.</summary>
    private static readonly Scalar AlbumPaper = new(150, 172, 228);

    /// <summary>
    /// An album page on the scanner bed: salmon paper, darker along its aged edges, with three straight photos stuck on
    /// it. The first reaches the page's top and right edges, as photos on a full page often do.
    /// </summary>
    /// <returns>The scan and each photo's rectangle.</returns>
    public static AlbumScan CreateAlbumPage(string path, int seed, int width = DefaultWidth, int height = DefaultHeight)
    {
        var random = new SeededRandom(seed);
        using var scan = CreateBed(random, width, height);

        var page = new Rect(width * 6 / 100, height * 5 / 100, width * 88 / 100, height * 85 / 100);
        using (var paper = new Mat(scan, page))
        {
            paper.SetTo(AlbumPaper);
            // Aged edges: up to 30 levels darker over the outer 3% of the page.
            int band = Math.Max(4, Math.Min(page.Width, page.Height) * 3 / 100);
            for (int d = 0; d < band; d++)
            {
                double darken = 30.0 * (1 - (double)d / band);
                var amount = new Scalar(darken, darken, darken);
                foreach (var strip in new[]
                {
                    new Rect(d, d, page.Width - 2 * d, 1), new Rect(d, page.Height - 1 - d, page.Width - 2 * d, 1),
                    new Rect(d, d, 1, page.Height - 2 * d), new Rect(page.Width - 1 - d, d, 1, page.Height - 2 * d),
                })
                {
                    using var line = new Mat(paper, strip);
                    Cv2.Subtract(line, amount, line);
                }
            }
            AddNoise(paper, random, 3);
        }

        Rect[] photos =
        [
            new(page.Right - page.Width * 40 / 100, page.Y, page.Width * 40 / 100, page.Height * 30 / 100),
            new(page.X + page.Width * 8 / 100, page.Y + page.Height * 38 / 100, page.Width * 38 / 100, page.Height * 26 / 100),
            new(page.X + page.Width * 50 / 100, page.Y + page.Height * 68 / 100, page.Width * 36 / 100, page.Height * 24 / 100),
        ];
        foreach (var rect in photos)
        {
            using var photo = CreatePhoto(random, rect.Width, rect.Height);
            using var target = new Mat(scan, rect);
            photo.CopyTo(target);
        }

        AddDust(scan, random);
        Cv2.ImWrite(path, scan);
        return new AlbumScan(path, photos);
    }

    /// <summary>
    /// A print with a white border, close to the scanner bed's colour, stuck on a torn piece of salmon album paper:
    /// against the bed only the picture inside the border stands out.
    /// </summary>
    /// <returns>The scan and the whole print's rectangle, border included.</returns>
    public static AlbumScan CreatePrintOnPaper(string path, int seed, int width = DefaultWidth, int height = DefaultHeight)
    {
        var random = new SeededRandom(seed);
        using var scan = CreateBed(random, width, height);
        // The print's white border: the bed's own colour (sampled away from the lid shadow).
        Scalar bedColour;
        using (var corner = new Mat(scan, new Rect(0, 0, width / 10, height / 10))) bedColour = Cv2.Mean(corner);

        // The torn paper: an irregular blob, so it is no rectangle of its own.
        var centre = new Point(width / 2, height / 2);
        var outline = Enumerable.Range(0, 24).Select(i =>
        {
            double a = i * Math.PI / 12, r = 0.85 + random.NextDouble() * 0.3;
            return new Point(centre.X + (int)(Math.Cos(a) * width * 0.42 * r), centre.Y + (int)(Math.Sin(a) * height * 0.36 * r));
        }).ToArray();
        using (var paperMask = new Mat(scan.Size(), MatType.CV_8UC1, Scalar.All(0)))
        {
            Cv2.FillPoly(paperMask, [outline], Scalar.All(255));
            using var paper = new Mat(scan.Size(), MatType.CV_8UC3, AlbumPaper);
            AddNoise(paper, random, 3);
            paper.CopyTo(scan, paperMask);
        }

        var print = new Rect(width * 30 / 100, height * 33 / 100, width * 40 / 100, height * 34 / 100);
        int border = print.Width * 6 / 100;
        using (var printArea = new Mat(scan, print))
        {
            printArea.SetTo(bedColour);
            AddNoise(printArea, random, 3);
            var picture = new Rect(border, border, print.Width - 2 * border, print.Height - 2 * border);
            using var photo = CreatePhoto(random, picture.Width, picture.Height);
            using var target = new Mat(printArea, picture);
            photo.CopyTo(target);
        }

        AddDust(scan, random);
        Cv2.ImWrite(path, scan);
        return new AlbumScan(path, [print]);
    }

    /// <summary>
    /// A dark bed with one print on it: a <paramref name="pictureWidth"/> × <paramref name="pictureHeight"/> picture on a
    /// white border <paramref name="border"/> pixels wide, its outside corner at (<paramref name="x"/>, <paramref name="y"/>).
    /// </summary>
    /// <returns>The scan and the picture's rectangle (inside the border).</returns>
    public static AlbumScan CreateBorderedPrintScan(string path, int seed, int pictureWidth = 540, int pictureHeight = 390, int border = 30, int x = 400, int y = 300)
    {
        var random = new SeededRandom(seed);
        int printWidth = pictureWidth + 2 * border, printHeight = pictureHeight + 2 * border;
        using var scan = new Mat(Math.Max(1200, y + printHeight + 200), Math.Max(1600, x + printWidth + 200), MatType.CV_8UC3, new Scalar(60, 60, 60));
        using (var print = new Mat(scan, new Rect(x, y, printWidth, printHeight))) print.SetTo(new Scalar(238, 240, 242));
        var picture = new Rect(x + border, y + border, pictureWidth, pictureHeight);
        using (var content = CreatePhoto(random, pictureWidth, pictureHeight))
        using (var target = new Mat(scan, picture))
        {
            content.CopyTo(target);
        }
        Cv2.ImWrite(path, scan);
        return new AlbumScan(path, [picture]);
    }

    /// <summary>A photo-like image: a two-colour gradient, a few soft shapes, and grain.</summary>
    private static Mat CreatePhoto(SeededRandom random, int width, int height)
    {
        var photo = new Mat(height, width, MatType.CV_8UC3);
        Scalar top = RandomColour(random), bottom = RandomColour(random);
        for (int y = 0; y < height; y++)
        {
            double t = (double)y / Math.Max(1, height - 1);
            using var row = new Mat(photo, new Rect(0, y, width, 1));
            row.SetTo(new Scalar(
                top.Val0 + (bottom.Val0 - top.Val0) * t,
                top.Val1 + (bottom.Val1 - top.Val1) * t,
                top.Val2 + (bottom.Val2 - top.Val2) * t));
        }

        int shapes = random.Next(3, 8);
        for (int i = 0; i < shapes; i++)
        {
            var centre = new Point(random.Next(width), random.Next(height));
            int radius = random.Next(Math.Max(4, width / 20), Math.Max(5, width / 5));
            Cv2.Circle(photo, centre, radius, RandomColour(random), -1, LineTypes.AntiAlias);
        }

        Cv2.GaussianBlur(photo, photo, new Size(0, 0), 2.5);
        AddNoise(photo, random, 6);
        return photo;
    }

    /// <summary>Mid-tone colours: clearly darker than the bed, so a photo's edge is always visible.</summary>
    private static Scalar RandomColour(SeededRandom random) =>
        new(random.Next(30, 190), random.Next(30, 190), random.Next(30, 190));

    private static void AddDust(Mat scan, SeededRandom random)
    {
        int specks = random.Next(4, 12);
        for (int i = 0; i < specks; i++)
        {
            var centre = new Point(random.Next(scan.Width), random.Next(scan.Height));
            double shade = random.Next(60, 140);
            Cv2.Circle(scan, centre, random.Next(1, 3), new Scalar(shade, shade, shade), -1, LineTypes.AntiAlias);
        }
    }

    /// <summary>Sensor-like grain, drawn from <paramref name="random"/> (not OpenCV's global generator) so a seed always gives the same image.</summary>
    private static void AddNoise(Mat image, SeededRandom random, int strength)
    {
        using var noise = new Mat(image.Size(), MatType.CV_16SC(image.Channels()));
        random.FillUniform(noise, Scalar.All(-strength), Scalar.All(strength + 1));
        using var wide = new Mat();
        image.ConvertTo(wide, MatType.CV_16S);
        Cv2.Add(wide, noise, wide);
        wide.ConvertTo(image, MatType.CV_8U); // saturates back into 0..255
    }
}
