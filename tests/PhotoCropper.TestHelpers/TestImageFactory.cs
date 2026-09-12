using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using System.Drawing;

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
        using Mat scan = new(height, width, DepthType.Cv8U, 3);
        scan.SetTo(new MCvScalar(255, 255, 255)); // White scanner background

        // Photo 1: Straight Black Square (400x400 at 100, 100)
        CvInvoke.Rectangle(scan, new Rectangle(100, 100, 400, 400), new MCvScalar(0, 0, 0), -1);

        // Photo 2: Tilted Dark Grey Polygon
        Point[] points = [
            new Point(1000, 1000),
            new Point(1400, 1100),
            new Point(1300, 1500),
            new Point(900, 1400)
        ];
        using var vp = new VectorOfPoint(points);
        CvInvoke.FillConvexPoly(scan, vp, new MCvScalar(50, 50, 50));

        scan.Save(path);
    }

    public static void CreateCornerPhotoScan(string path)
    {
        using Mat scan = new(2000, 2000, DepthType.Cv8U, 3);
        scan.SetTo(new MCvScalar(255, 255, 255));

        // Photo 1 in center
        CvInvoke.Rectangle(scan, new Rectangle(800, 800, 400, 400), new MCvScalar(0, 0, 0), -1);

        // Photo 2 in corner touching bottom-right
        CvInvoke.Rectangle(scan, new Rectangle(1700, 1700, 300, 300), new MCvScalar(20, 20, 20), -1);

        scan.Save(path);
    }

    public static void CreateFlushEdgeScan(string path)
    {
        using Mat scan = new(2000, 2000, DepthType.Cv8U, 3);
        scan.SetTo(new MCvScalar(255, 255, 255));

        // Photo touching top-left edge
        CvInvoke.Rectangle(scan, new Rectangle(0, 0, 500, 400), new MCvScalar(0, 0, 0), -1);

        scan.Save(path);
    }

    public static void CreateTiltedAdjacentScan(string path)
    {
        using Mat scan = new(2000, 2000, DepthType.Cv8U, 3);
        scan.SetTo(new MCvScalar(255, 255, 255));

        // Photo 1: Large diamond shape centered at (1000, 1000)
        Point[] points1 = [
            new Point(1000, 646),
            new Point(1354, 1000),
            new Point(1000, 1354),
            new Point(646, 1000)
        ];
        using (var vp1 = new VectorOfPoint(points1))
        {
            CvInvoke.FillConvexPoly(scan, vp1, new MCvScalar(50, 50, 50), LineType.AntiAlias);
        }

        // Photo 2: Square 220x220 at (550, 550)
        CvInvoke.Rectangle(scan, new Rectangle(550, 550, 220, 220), new MCvScalar(20, 20, 20), -1);

        scan.Save(path);
    }

    public static void CreateCloselySpacedScan(string path, int gap = 20)
    {
        using Mat scan = new(2000, 2000, DepthType.Cv8U, 3);
        scan.SetTo(new MCvScalar(255, 255, 255));

        // Photo 1: 500x500 at (200, 200)
        CvInvoke.Rectangle(scan, new Rectangle(200, 200, 500, 500), new MCvScalar(20, 20, 20), -1);

        // Photo 2: 500x500 at (200 + 500 + gap, 200)
        CvInvoke.Rectangle(scan, new Rectangle(700 + gap, 200, 500, 500), new MCvScalar(30, 30, 30), -1);

        scan.Save(path);
    }

    public static void CreateLowContrastScan(string path)
    {
        using Mat scan = new(2000, 2000, DepthType.Cv8U, 3);
        scan.SetTo(new MCvScalar(240, 240, 240));

        // Subtle low-contrast photo
        CvInvoke.Rectangle(scan, new Rectangle(300, 300, 600, 600), new MCvScalar(232, 230, 228), -1);

        scan.Save(path);
    }

    public static void CreateDarkBackgroundScan(string path)
    {
        using Mat scan = new(2000, 2000, DepthType.Cv8U, 3);
        scan.SetTo(new MCvScalar(30, 30, 30));

        // Bright photo
        CvInvoke.Rectangle(scan, new Rectangle(200, 200, 600, 400), new MCvScalar(220, 200, 180), -1);

        scan.Save(path);
    }

    public static void CreateHighResScan(string path)
    {
        using Mat scan = new(5000, 5000, DepthType.Cv8U, 3);
        scan.SetTo(new MCvScalar(255, 255, 255));

        CvInvoke.Rectangle(scan, new Rectangle(1000, 1000, 3000, 3000), new MCvScalar(0, 0, 0), -1);

        scan.Save(path);
    }

    public static Mat CreateSkyLandscapePhoto(int width = 200, int height = 200, int orientation = 0)
    {
        Mat mat = new(height, width, DepthType.Cv8U, 3);
        MCvScalar skyColor = new(235, 180, 70); // BGR Blue Sky
        MCvScalar groundColor = new(20, 40, 20); // Dark Green Ground

        switch (orientation)
        {
            case 180: // Upside down: sky at bottom, ground at top
                CvInvoke.Rectangle(mat, new Rectangle(0, 0, width, height / 2), groundColor, -1);
                CvInvoke.Rectangle(mat, new Rectangle(0, height / 2, width, height / 2), skyColor, -1);
                break;
            case 90: // Sideways: sky on left, ground on right
                CvInvoke.Rectangle(mat, new Rectangle(0, 0, width / 2, height), skyColor, -1);
                CvInvoke.Rectangle(mat, new Rectangle(width / 2, 0, width / 2, height), groundColor, -1);
                break;
            case 270: // Sideways: sky on right, ground on left
                CvInvoke.Rectangle(mat, new Rectangle(0, 0, width / 2, height), groundColor, -1);
                CvInvoke.Rectangle(mat, new Rectangle(width / 2, 0, width / 2, height), skyColor, -1);
                break;
            default: // Upright: sky at top, ground at bottom
                CvInvoke.Rectangle(mat, new Rectangle(0, 0, width, height / 2), skyColor, -1);
                CvInvoke.Rectangle(mat, new Rectangle(0, height / 2, width, height / 2), groundColor, -1);
                break;
        }

        return mat;
    }

    public static Mat CreateFadedPhoto(int width = 200, int height = 200, bool includeAlpha = false)
    {
        Mat mat = new(height, width, DepthType.Cv8U, includeAlpha ? 4 : 3);
        mat.SetTo(includeAlpha ? new MCvScalar(50, 150, 200, 255) : new MCvScalar(50, 150, 200));
        return mat;
    }

    public static Mat CreateBlemishedPhoto(int width = 200, int height = 200)
    {
        Mat mat = new(height, width, DepthType.Cv8U, 3);
        mat.SetTo(new MCvScalar(128, 128, 128));

        // Inject simulated dust speck (bright white dot) and dark hairline scratch
        CvInvoke.Circle(mat, new Point(50, 50), 2, new MCvScalar(255, 255, 255), -1);
        CvInvoke.Line(mat, new Point(100, 100), new Point(108, 108), new MCvScalar(0, 0, 0), 1);

        return mat;
    }
}
