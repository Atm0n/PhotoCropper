using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Core.Export;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Core.Tests.Export;

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
        sourceDpi.XDpi.ShouldBe(600);
        sourceDpi.YDpi.ShouldBe(600);

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

        progressUpdates.ShouldBe(1);
        string[] exportedFiles = Directory.GetFiles(exportDir, "*.jpg");
        exportedFiles.Length.ShouldBe(1);

        var exportedDpi = PhotoExporter.GetDpiFromSource(exportedFiles[0]);
        exportedDpi.XDpi.ShouldBe(600);
        exportedDpi.YDpi.ShouldBe(600);
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
        exportedPngs.Length.ShouldBe(1);
    }

    [Fact]
    public void SavePhotos_AndEngine_ShouldSupportUnicodeAndAccentedPaths()
    {
        string unicodeScanPath = Path.Combine(_tempDir, "IMG_2026_còpia (24) - 日本語_тест.jpg");
        using (Mat scan = new(200, 200, DepthType.Cv8U, 3))
        {
            scan.SetTo(new MCvScalar(128, 128, 128));
            using Emgu.CV.Util.VectorOfByte buf = new();
            CvInvoke.Imencode(".jpg", scan, buf);
            File.WriteAllBytes(unicodeScanPath, buf.ToArray());
        }

        using (PhotoCropperEngine engine = new(unicodeScanPath))
        {
            engine.Original.IsEmpty.ShouldBeFalse();
            engine.Original.Width.ShouldBe(200);
            engine.Original.Height.ShouldBe(200);
        }

        using Mat photo = new(50, 50, DepthType.Cv8U, 3);
        photo.SetTo(new MCvScalar(255, 0, 0));

        string exportDir = Path.Combine(_tempDir, "export_còpia_folder");
        PhotoExporter.SavePhotos([photo], unicodeScanPath, exportDir, "JPEG", 90);

        string[] exported = Directory.GetFiles(exportDir, "*.jpg");
        exported.Length.ShouldBe(1);
        Path.GetFileName(exported[0]).ShouldContain("còpia");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
