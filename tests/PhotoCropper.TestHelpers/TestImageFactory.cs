using OpenCvSharp;

namespace PhotoCropper.TestHelpers;

public static class TestImageFactory
{
    public static string CreateTempDirectory(string prefix = "PhotoCropperTest")
    {
        string path = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    public static void CreateStandardTwoPhotoScan(string path, int width = 2000, int height = 2000)
    {
        using Mat scan = new(height, width, MatType.CV_8UC3);
        scan.SetTo(new Scalar(255, 255, 255)); // White scanner background

        // Photo 1: Straight Black Square (400x400 at 100, 100)
        Cv2.Rectangle(scan, new Rect(100, 100, 400, 400), new Scalar(0, 0, 0), -1);

        // Photo 2: Tilted Dark Grey Polygon
        Point[] points = [
            new Point(1000, 1000),
            new Point(1400, 1100),
            new Point(1300, 1500),
            new Point(900, 1400)
        ];
        Point[] vp = points;
        Cv2.FillConvexPoly(scan, vp, new Scalar(50, 50, 50));

        Cv2.ImWrite(path, scan);
    }

    public static void CreateCornerPhotoScan(string path)
    {
        using Mat scan = new(2000, 2000, MatType.CV_8UC3);
        scan.SetTo(new Scalar(255, 255, 255));

        // Photo 1 in center
        Cv2.Rectangle(scan, new Rect(800, 800, 400, 400), new Scalar(0, 0, 0), -1);

        // Photo 2 in corner touching bottom-right
        Cv2.Rectangle(scan, new Rect(1700, 1700, 300, 300), new Scalar(20, 20, 20), -1);

        Cv2.ImWrite(path, scan);
    }

    public static void CreateFlushEdgeScan(string path)
    {
        using Mat scan = new(2000, 2000, MatType.CV_8UC3);
        scan.SetTo(new Scalar(255, 255, 255));

        // Photo touching top-left edge
        Cv2.Rectangle(scan, new Rect(0, 0, 500, 400), new Scalar(0, 0, 0), -1);

        Cv2.ImWrite(path, scan);
    }

    public static void CreateTiltedAdjacentScan(string path)
    {
        using Mat scan = new(2000, 2000, MatType.CV_8UC3);
        scan.SetTo(new Scalar(255, 255, 255));

        // Photo 1: Large diamond shape centered at (1000, 1000)
        Point[] points1 = [
            new Point(1000, 646),
            new Point(1354, 1000),
            new Point(1000, 1354),
            new Point(646, 1000)
        ];
        {
            Point[] vp1 = points1;
            Cv2.FillConvexPoly(scan, vp1, new Scalar(50, 50, 50), LineTypes.AntiAlias);
        }

        // Photo 2: Square 220x220 at (550, 550)
        Cv2.Rectangle(scan, new Rect(550, 550, 220, 220), new Scalar(20, 20, 20), -1);

        Cv2.ImWrite(path, scan);
    }

    public static void CreateCloselySpacedScan(string path, int gap = 20)
    {
        using Mat scan = new(2000, 2000, MatType.CV_8UC3);
        scan.SetTo(new Scalar(255, 255, 255));

        // Photo 1: 500x500 at (200, 200)
        Cv2.Rectangle(scan, new Rect(200, 200, 500, 500), new Scalar(20, 20, 20), -1);

        // Photo 2: 500x500 at (200 + 500 + gap, 200)
        Cv2.Rectangle(scan, new Rect(700 + gap, 200, 500, 500), new Scalar(30, 30, 30), -1);

        Cv2.ImWrite(path, scan);
    }

    public static void CreateLowContrastScan(string path)
    {
        using Mat scan = new(2000, 2000, MatType.CV_8UC3);
        scan.SetTo(new Scalar(240, 240, 240));

        // Subtle low-contrast photo
        Cv2.Rectangle(scan, new Rect(300, 300, 600, 600), new Scalar(232, 230, 228), -1);

        Cv2.ImWrite(path, scan);
    }

    public static void CreateDarkBackgroundScan(string path)
    {
        using Mat scan = new(2000, 2000, MatType.CV_8UC3);
        scan.SetTo(new Scalar(30, 30, 30));

        // Bright photo
        Cv2.Rectangle(scan, new Rect(200, 200, 600, 400), new Scalar(220, 200, 180), -1);

        Cv2.ImWrite(path, scan);
    }

    /// <summary>The photos on <see cref="CreateAlbumPageOnBedScan"/>'s page, in scan pixels.</summary>
    public static readonly Rect[] AlbumPagePhotos = [new(160, 160, 600, 800), new(900, 200, 640, 460), new(160, 1200, 700, 500), new(1000, 1300, 480, 700)];

    /// <summary>
    /// An album page lying on a light scanner bed, with a 60 px ring of bed around it: grey paper (<paramref name="paperGrey"/>)
    /// that darkens by <paramref name="edgeDarken"/> towards its edge, with four textured photos on it and some noise.
    /// </summary>
    public static void CreateAlbumPageOnBedScan(string path, int paperGrey = 94, int edgeDarken = 14)
    {
        const int w = 1700, h = 2340, margin = 60;
        var pixels = new byte[w * h * 3];
        int x0 = margin, y0 = margin, x1 = w - margin, y1 = h - margin - 40;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double v;
                if (x >= x0 && x < x1 && y >= y0 && y < y1)
                {
                    int d = Math.Min(Math.Min(x - x0, x1 - 1 - x), Math.Min(y - y0, y1 - 1 - y));
                    v = paperGrey - edgeDarken * Math.Clamp(1 - d / 70.0, 0, 1);
                    foreach (var r in AlbumPagePhotos)
                    {
                        // A photo: a light border round a picture with some structure.
                        if (!r.Contains(x, y)) continue;
                        bool border = x < r.X + 25 || x >= r.Right - 25 || y < r.Y + 25 || y >= r.Bottom - 25;
                        v = border ? 225 : 120 + 70 * Math.Sin(x / 37.0) * Math.Cos(y / 53.0);
                    }
                }
                else
                {
                    v = 246 - 10.0 * y / h; // the bed, with the lid's gradient
                }
                for (int c = 0; c < 3; c++)
                {
                    // Noise with a 2.2 spread, as a scanner gives.
                    double noise = PixelNoise(x, y, c) * 2.2;
                    pixels[(y * w + x) * 3 + c] = (byte)Math.Clamp(Math.Round(v + noise + (c == 0 ? 2 : 0)), 0, 255);
                }
            }
        }
        using Mat flat = new(h, w * 3, MatType.CV_8UC1);
        flat.SetArray(pixels);
        using Mat scan = flat.Reshape(3, h);
        Cv2.ImWrite(path, scan);
    }

    /// <summary>The prints on <see cref="CreatePrintsReachingTheEdgeScan"/>, in scan pixels.</summary>
    public static readonly Rect[] PrintsReachingTheEdge = [new(30, 120, 1640, 980), new(30, 1140, 1640, 1200)];

    /// <summary>
    /// Two large prints lying on a light scanner bed, nearly as wide as it; the lower one runs off the scan's bottom
    /// edge, where the middle of its picture is a white floor. Of the spots the background is usually sampled at,
    /// the top ones fall on the bed and the bottom middle one on the floor; the others on the lower print.
    /// </summary>
    public static void CreatePrintsReachingTheEdgeScan(string path)
    {
        const int w = 1700, h = 2340;
        var pixels = new byte[w * h * 3];
        var lower = PrintsReachingTheEdge[1];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double v = 240; // the bed
                if (PrintsReachingTheEdge[0].Contains(x, y)) v = 150 + 60 * Math.Sin(x / 41.0) * Math.Cos(y / 57.0);
                if (lower.Contains(x, y))
                {
                    v = 100 + 60 * Math.Sin(x / 41.0) * Math.Cos(y / 57.0); // a picture with some structure
                    // The white floor at the bottom of the lower print, reaching the scan's edge.
                    if (y >= h - 260 && x >= 450 && x < 1250) v = 238;
                }
                for (int c = 0; c < 3; c++)
                {
                    pixels[(y * w + x) * 3 + c] = (byte)Math.Clamp(Math.Round(v + PixelNoise(x, y, c) * 2.2), 0, 255);
                }
            }
        }
        using Mat flat = new(h, w * 3, MatType.CV_8UC1);
        flat.SetArray(pixels);
        using Mat scan = flat.Reshape(3, h);
        Cv2.ImWrite(path, scan);
    }

    /// <summary>
    /// Noise for one pixel's channel, roughly normal with mean 0 and spread 1, worked out from its position alone: the
    /// same image every run, and no shared generator between tests running in parallel.
    /// </summary>
    private static double PixelNoise(int x, int y, int channel)
    {
        // A 64-bit hash of the position (SplitMix64's finaliser), split into four uniform values in [0, 1).
        ulong z = ((ulong)(uint)x << 34) ^ ((ulong)(uint)y << 2) ^ (uint)channel;
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        double sum = 0;
        for (int i = 0; i < 4; i++) sum += ((z >> (16 * i)) & 0xFFFF) / 65536.0;
        // The sum of four uniform values has mean 2 and variance 4/12: centred and scaled to spread 1.
        return (sum - 2) * Math.Sqrt(3);
    }

    public static void CreateHighResScan(string path)
    {
        using Mat scan = new(5000, 5000, MatType.CV_8UC3);
        scan.SetTo(new Scalar(255, 255, 255));

        Cv2.Rectangle(scan, new Rect(1000, 1000, 3000, 3000), new Scalar(0, 0, 0), -1);

        Cv2.ImWrite(path, scan);
    }

    public static Mat CreateSkyLandscapePhoto(int width = 200, int height = 200, int orientation = 0)
    {
        Mat mat = new(height, width, MatType.CV_8UC3);
        Scalar skyColor = new(235, 180, 70); // BGR Blue Sky
        Scalar groundColor = new(20, 40, 20); // Dark Green Ground

        switch (orientation)
        {
            case 180: // Upside down: sky at bottom, ground at top
                Cv2.Rectangle(mat, new Rect(0, 0, width, height / 2), groundColor, -1);
                Cv2.Rectangle(mat, new Rect(0, height / 2, width, height / 2), skyColor, -1);
                break;
            case 90: // Sideways: sky on left, ground on right
                Cv2.Rectangle(mat, new Rect(0, 0, width / 2, height), skyColor, -1);
                Cv2.Rectangle(mat, new Rect(width / 2, 0, width / 2, height), groundColor, -1);
                break;
            case 270: // Sideways: sky on right, ground on left
                Cv2.Rectangle(mat, new Rect(0, 0, width / 2, height), groundColor, -1);
                Cv2.Rectangle(mat, new Rect(width / 2, 0, width / 2, height), skyColor, -1);
                break;
            default: // Upright: sky at top, ground at bottom
                Cv2.Rectangle(mat, new Rect(0, 0, width, height / 2), skyColor, -1);
                Cv2.Rectangle(mat, new Rect(0, height / 2, width, height / 2), groundColor, -1);
                break;
        }

        return mat;
    }

    public static Mat CreateFadedPhoto(int width = 200, int height = 200, bool includeAlpha = false)
    {
        Mat mat = new(height, width, includeAlpha ? MatType.CV_8UC4 : MatType.CV_8UC3);
        mat.SetTo(includeAlpha ? new Scalar(50, 150, 200, 255) : new Scalar(50, 150, 200));
        return mat;
    }

    public static Mat CreateBlemishedPhoto(int width = 200, int height = 200)
    {
        Mat mat = new(height, width, MatType.CV_8UC3);
        mat.SetTo(new Scalar(128, 128, 128));

        // Inject simulated dust speck (bright white dot) and dark hairline scratch
        Cv2.Circle(mat, new Point(50, 50), 2, new Scalar(255, 255, 255), -1);
        Cv2.Line(mat, new Point(100, 100), new Point(108, 108), new Scalar(0, 0, 0), 1);

        return mat;
    }

    public static void CreateScannerBezelScan(string path, int bezelThickness = 15, int width = 2000, int height = 2000)
    {
        using Mat scan = new(height, width, MatType.CV_8UC3);
        scan.SetTo(new Scalar(255, 255, 255)); // White scanner background

        // Photo 1: Straight Dark Square (400x400 at 200, 200)
        Cv2.Rectangle(scan, new Rect(200, 200, 400, 400), new Scalar(20, 20, 20), -1);

        // Photo 2: Dark Rect (400x300 at 1000, 1000)
        Cv2.Rectangle(scan, new Rect(1000, 1000, 400, 300), new Scalar(40, 40, 40), -1);

        // Simulated scanner dark bezel around the perimeter (typical of flatbed scanners like Canon LiDE 400)
        if (bezelThickness > 0)
        {
            Scalar bezelColor = new(10, 10, 10);
            Cv2.Rectangle(scan, new Rect(0, 0, width, bezelThickness), bezelColor, -1); // Top
            Cv2.Rectangle(scan, new Rect(0, height - bezelThickness, width, bezelThickness), bezelColor, -1); // Bottom
            Cv2.Rectangle(scan, new Rect(0, 0, bezelThickness, height), bezelColor, -1); // Left
            Cv2.Rectangle(scan, new Rect(width - bezelThickness, 0, bezelThickness, height), bezelColor, -1); // Right
        }

        Cv2.ImWrite(path, scan);
    }
}
