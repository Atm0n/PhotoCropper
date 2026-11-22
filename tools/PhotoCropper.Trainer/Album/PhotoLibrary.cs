using OpenCvSharp;

namespace PhotoCropper.Trainer.Album;

/// <summary>
/// The pictures that go into the generated photos: real public-domain photos from folders, and pictures made up on
/// the spot (sky, ground, blurred shapes, grain) so the network never sees only a few dozen real ones. Every picture
/// is cropped, flipped and toned differently each time it is used.
/// </summary>
internal sealed class PhotoLibrary : IDisposable
{
    private const int MaxSide = 900;
    private readonly List<Mat> _photos = [];

    /// <summary>Loads the JPEG and PNG files in <paramref name="folders"/> (and their subfolders).</summary>
    public static PhotoLibrary Load(IEnumerable<string> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        var library = new PhotoLibrary();
        try
        {
            var files = folders
                .Where(Directory.Exists)
                .SelectMany(f => Directory.EnumerateFiles(f, "*.*", SearchOption.AllDirectories))
                .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)
                .ToList();
            var loaded = new Mat?[files.Count];
            Parallel.For(0, files.Count, i => loaded[i] = LoadShrunk(files[i]));
            library._photos.AddRange(loaded.OfType<Mat>());
            return library;
        }
        catch
        {
            library.Dispose();
            throw;
        }
    }

    private static Mat? LoadShrunk(string path)
    {
        using Mat image = Cv2.ImRead(path, ImreadModes.Color);
        if (image.Empty() || image.Width < 64 || image.Height < 64) return null;
        double scale = Math.Min(1.0, (double)MaxSide / Math.Max(image.Width, image.Height));
        var shrunk = new Mat();
        Cv2.Resize(image, shrunk, new Size(Math.Round(image.Width * scale), Math.Round(image.Height * scale)), 0, 0, InterpolationFlags.Area);
        return shrunk;
    }

    /// <summary>How many real photos were loaded.</summary>
    public int Count => _photos.Count;

    /// <summary>
    /// A picture of <paramref name="width"/> × <paramref name="height"/> (8-bit BGR): a random crop of a real photo,
    /// or a made-up one (always when there are no real photos, otherwise about one time in seven).
    /// </summary>
    public Mat Take(Rng rng, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(rng);
        if (_photos.Count == 0 || rng.Chance(0.15)) return MadeUp(rng, width, height);

        Mat source = rng.Pick(_photos);
        // Keep the middle of scanned prints: their own scans often have a margin of mount or background.
        double keep = rng.Range(0.55, 0.88);
        double aspect = (double)width / height;
        double cropW = source.Width * keep, cropH = cropW / aspect;
        if (cropH > source.Height * keep)
        {
            cropH = source.Height * keep;
            cropW = cropH * aspect;
        }
        int x = (int)rng.Range(0, Math.Max(1, source.Width - cropW));
        int y = (int)rng.Range(0, Math.Max(1, source.Height - cropH));
        var rect = new Rect(x, y, Math.Max(2, (int)cropW), Math.Max(2, (int)cropH)).Intersect(new Rect(0, 0, source.Width, source.Height));

        using Mat crop = source[rect];
        var result = new Mat();
        Cv2.Resize(crop, result, new Size(width, height), 0, 0, width < crop.Width ? InterpolationFlags.Area : InterpolationFlags.Cubic);
        if (rng.Chance(0.5)) Cv2.Flip(result, result, FlipMode.Y);
        return result;
    }

    /// <summary>
    /// A made-up picture with a photo's kind of detail: a sky and ground with a horizon, shapes of varied size and
    /// colour (people, trees, buildings), blurred to different depths, and film grain.
    /// </summary>
    private static Mat MadeUp(Rng rng, int width, int height)
    {
        var image = new Mat(height, width, MatType.CV_8UC3);
        Scalar sky = RandomColour(rng, 90, 235), ground = RandomColour(rng, 30, 170);
        int horizon = (int)(height * rng.Range(0.25, 0.75));
        for (int row = 0; row < height; row++)
        {
            // Each half darkens downwards, as sky towards the horizon and ground towards the camera.
            double t = row < horizon ? (double)row / Math.Max(1, horizon) : (double)(row - horizon) / Math.Max(1, height - horizon);
            Scalar top = row < horizon ? sky : ground;
            double shade = 1.0 - (0.35 * t);
            Cv2.Line(image, new Point(0, row), new Point(width - 1, row), new Scalar(top.Val0 * shade, top.Val1 * shade, top.Val2 * shade));
        }

        int shapes = rng.Range(8, 30);
        for (int i = 0; i < shapes; i++)
        {
            var centre = new Point(rng.Range(0, width), rng.Range(horizon / 2, height));
            var axes = new Size(rng.Range(width / 30 + 1, width / 4 + 2), rng.Range(height / 20 + 1, height / 3 + 2));
            Scalar colour = RandomColour(rng, 10, 245);
            if (rng.Chance(0.5))
            {
                Cv2.Ellipse(image, centre, axes, rng.Range(0, 180), 0, 360, colour, -1, LineTypes.AntiAlias);
            }
            else
            {
                Cv2.Rectangle(image, new Rect(centre.X - axes.Width / 2, centre.Y - axes.Height, axes.Width, axes.Height * 2), colour, -1);
            }
        }

        Cv2.GaussianBlur(image, image, new Size(0, 0), rng.Range(0.6, 2.5));
        // Fine detail everywhere, as real scenes have (leaves, fabric, gravel), then film grain.
        using Mat detail = Noise.Blotches(new Size(width, height), rng, rng.Range(8, 22), (int)rng.Range(2, 6));
        using Mat detail8 = new();
        detail.ConvertTo(detail8, MatType.CV_8UC1, 1, 128);
        using Mat detail3 = new();
        Cv2.Merge([detail8, detail8, detail8], detail3);
        Cv2.AddWeighted(image, 1.0, detail3, 1.0, -128, image);
        using Mat grain = new(height, width, MatType.CV_8UC3);
        Noise.FillNormal(grain, rng, 128, rng.Range(5, 14));
        Cv2.AddWeighted(image, 1.0, grain, 1.0, -128, image);
        return image;
    }

    private static Scalar RandomColour(Rng rng, int min, int max) => new(rng.Range(min, max), rng.Range(min, max), rng.Range(min, max));

    public void Dispose()
    {
        foreach (var photo in _photos) photo.Dispose();
        _photos.Clear();
    }
}
