using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Export;
using PhotoCropper.Tests.Helpers;

namespace PhotoCropper.Tests.Export;

public sealed class PhotoExporterTests : IDisposable
{
    private readonly string _tempDir;

    public PhotoExporterTests()
    {
        _tempDir = TestImageFactory.CreateTempDirectory("PhotoExporterTests");
    }

    [Fact]
    public void PhotoExporter_ShouldPreserveDpiAndReportProgress()
    {
        string scanPath = Path.Combine(_tempDir, "dpi_test_scan.jpg");
        using (Mat scan = new(300, 300, DepthType.Cv8U, 3))
        {
            scan.SetTo(new MCvScalar(200, 200, 200));
            scan.Save(scanPath);
        }

        // Set source JPEG DPI to 600x600
        PhotoExporter.EmbedJpegDpi(scanPath, 600, 600);
        var sourceDpi = PhotoExporter.GetDpiFromSource(scanPath);
        Assert.Equal(600, sourceDpi.XDpi);
        Assert.Equal(600, sourceDpi.YDpi);

        using Mat photo1 = new(100, 100, DepthType.Cv8U, 3);
        photo1.SetTo(new MCvScalar(50, 50, 50));

        int progressUpdates = 0;
        string exportDir = Path.Combine(_tempDir, "dpi_export");

        PhotoExporter.SavePhotos(
            [photo1],
            scanPath,
            exportDir,
            "JPEG",
            90,
            (done, total) => { progressUpdates++; });

        Assert.Equal(1, progressUpdates);
        string[] exportedFiles = Directory.GetFiles(exportDir, "*.jpg");
        Assert.Single(exportedFiles);

        var exportedDpi = PhotoExporter.GetDpiFromSource(exportedFiles[0]);
        Assert.Equal(600, exportedDpi.XDpi);
        Assert.Equal(600, exportedDpi.YDpi);
    }

    [Fact]
    public void SavePhotos_PNG_ShouldCreateLosslessFiles()
    {
        string scanPath = Path.Combine(_tempDir, "png_test_scan.jpg");
        using (Mat scan = new(300, 300, DepthType.Cv8U, 3))
        {
            scan.SetTo(new MCvScalar(200, 200, 200));
            scan.Save(scanPath);
        }

        using Mat photo1 = new(100, 100, DepthType.Cv8U, 3);
        photo1.SetTo(new MCvScalar(50, 50, 50));

        string exportDir = Path.Combine(_tempDir, "png_export");
        PhotoExporter.SavePhotos([photo1], scanPath, exportDir, "PNG", 90);

        string[] exportedPngs = Directory.GetFiles(exportDir, "*.png");
        Assert.Single(exportedPngs);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
