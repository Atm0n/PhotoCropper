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
    public void SavePhotos_WithCleanOldExports_ShouldDeletePreviousFiles()
    {
        string outputDir = Path.Combine(_tempDir, "CleanTest");
        Directory.CreateDirectory(outputDir);
        string scanPath = Path.Combine(_tempDir, "batch_scan.jpg");
        using (Mat scan = new(300, 300, DepthType.Cv8U, 3))
        {
            scan.SetTo(new MCvScalar(200, 200, 200));
            scan.Save(scanPath);
        }
        
        string oldFile1 = Path.Combine(outputDir, "batch_scan_1.jpg");
        string oldFile2 = Path.Combine(outputDir, "batch_scan_2.jpg");
        File.WriteAllText(oldFile1, "old");
        File.WriteAllText(oldFile2, "old");

        using Mat photo = new(10, 10, DepthType.Cv8U, 3);
        photo.SetTo(new MCvScalar(0, 0, 0));

        PhotoExporter.SavePhotos([photo], scanPath, customOutputFolder: outputDir, cleanOldExports: true);

        File.Exists(oldFile2).ShouldBeFalse("Old file 2 should have been deleted");
        
        // oldFile1 was overwritten, let's verify it contains the actual image, not "old"
        var newFile1Bytes = File.ReadAllBytes(oldFile1);
        newFile1Bytes.Length.ShouldBeGreaterThan(3);
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

    [Fact]
    public void SavePhotos_WhenSourceIsInRawScansDirectory_ShouldExportToCroppedSiblingDirectory()
    {
        string workspaceDir = Path.Combine(_tempDir, "workspace");
        string rawScansDir = Path.Combine(workspaceDir, "RawScans");
        Directory.CreateDirectory(rawScansDir);

        string scanFile = Path.Combine(rawScansDir, "scan_0001.jpg");
        using (Mat scan = new(100, 100, DepthType.Cv8U, 3))
        {
            scan.SetTo(new MCvScalar(200, 200, 200));
            scan.Save(scanFile);
        }

        using Mat photo = new(50, 50, DepthType.Cv8U, 3);
        photo.SetTo(new MCvScalar(100, 100, 100));

        // When customOutputFolder is null, it should detect RawScans and export to workspace/Cropped
        PhotoExporter.SavePhotos([photo], scanFile, customOutputFolder: null, "JPEG", 90);

        string expectedCroppedDir = Path.Combine(workspaceDir, "Cropped");
        Directory.Exists(expectedCroppedDir).ShouldBeTrue();
        string[] files = Directory.GetFiles(expectedCroppedDir, "*.jpg");
        files.Length.ShouldBe(1);
        Path.GetFileName(files[0]).ShouldBe("scan_0001_1.jpg");

        // Ensure no nested rawScans/cropped was created
        Directory.Exists(Path.Combine(rawScansDir, "cropped")).ShouldBeFalse();
    }

    [Fact]
    public void SavePhotos_WithCustomPatternAndMetadata_ShouldProduceFormattedNameAndEmbedExif()
    {
        string scanFile = Path.Combine(_tempDir, "vintage_scan.jpg");
        using (Mat scan = new(100, 100, DepthType.Cv8U, 3))
        {
            scan.SetTo(new MCvScalar(150, 150, 150));
            scan.Save(scanFile);
        }

        using Mat photo1 = new(40, 40, DepthType.Cv8U, 3);
        using Mat photo2 = new(40, 40, DepthType.Cv8U, 3);
        photo1.SetTo(new MCvScalar(20, 20, 20));
        photo2.SetTo(new MCvScalar(40, 40, 40));

        string exportDir = Path.Combine(_tempDir, "pattern_meta_export");
        var metadata = new PhotoCropper.Core.Models.PhotoExportMetadata
        {
            Year = 1978,
            Description = "Grandparents anniversary"
        };

        PhotoExporter.SavePhotos(
            [photo1, photo2],
            scanFile,
            exportDir,
            "JPEG",
            90,
            fileNamePattern: "{year}_{original}_{index:02}",
            metadata: metadata);

        string[] exported = Directory.GetFiles(exportDir, "*.jpg");
        Array.Sort(exported);
        exported.Length.ShouldBe(2);

        string file1 = Path.GetFileName(exported[0]);
        string file2 = Path.GetFileName(exported[1]);

        file1.ShouldBe("1978_vintage_scan_01.jpg");
        file2.ShouldBe("1978_vintage_scan_02.jpg");

        // Verify EXIF metadata in exported file
        byte[] bytes = File.ReadAllBytes(exported[0]);
        string text = System.Text.Encoding.ASCII.GetString(bytes);
        text.ShouldContain("1978:01:01 00:00:00");
        text.ShouldContain("Grandparents anniversary");
    }

    [Fact]
    public void ResolveUniqueExportPath_WhenTargetClaimedOrExists_ShouldDisambiguate()
    {
        PhotoExporter.ClearClaimedExportPaths();
        string testFile = Path.Combine(_tempDir, "collision_test.jpg");

        string path1 = PhotoExporter.ResolveUniqueExportPath(testFile);
        string path2 = PhotoExporter.ResolveUniqueExportPath(testFile);
        string path3 = PhotoExporter.ResolveUniqueExportPath(testFile);

        Path.GetFileName(path1).ShouldBe("collision_test.jpg");
        Path.GetFileName(path2).ShouldBe("collision_test (1).jpg");
        Path.GetFileName(path3).ShouldBe("collision_test (2).jpg");
    }

    [Fact]
    public void SavePhotos_ConcurrentScansWithSameNamingPattern_ShouldExportAllPhotosWithoutCollision()
    {
        PhotoExporter.ClearClaimedExportPaths();
        string exportDir = Path.Combine(_tempDir, "concurrent_export");

        string scan1 = Path.Combine(_tempDir, "scan_alpha.jpg");
        string scan2 = Path.Combine(_tempDir, "scan_beta.jpg");
        File.WriteAllBytes(scan1, [0xFF, 0xD8, 0xFF, 0xD9]);
        File.WriteAllBytes(scan2, [0xFF, 0xD8, 0xFF, 0xD9]);

        using Mat photo1 = new(40, 40, DepthType.Cv8U, 3);
        using Mat photo2 = new(40, 40, DepthType.Cv8U, 3);
        photo1.SetTo(new MCvScalar(10, 10, 10));
        photo2.SetTo(new MCvScalar(20, 20, 20));

        var meta = new PhotoCropper.Core.Models.PhotoExportMetadata { Year = 2002 };

        // Run both scans concurrently using a pattern that lacks {original}
        Parallel.Invoke(
            () => PhotoExporter.SavePhotos([photo1], scan1, exportDir, "JPEG", 90, "{year}_{index:02}", meta),
            () => PhotoExporter.SavePhotos([photo2], scan2, exportDir, "JPEG", 90, "{year}_{index:02}", meta)
        );

        string[] exported = Directory.GetFiles(exportDir, "*.jpg");
        exported.Length.ShouldBe(2);

        var names = exported.Select(Path.GetFileName).ToList();
        names.ShouldContain("2002_01.jpg");
        names.ShouldContain("2002_01 (1).jpg");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
