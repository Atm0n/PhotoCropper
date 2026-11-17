using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Core.Export;
using PhotoCropper.Core.Models;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Core.Tests.Export;

/// <summary>
/// Re-export cleanup must only remove files this scan previously exported, never unrelated files.
/// </summary>
public sealed class ExportCleanupTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _outputDir;

    public ExportCleanupTests()
    {
        _tempDir = TestImageFactory.CreateTempDirectory("ExportCleanupTests");
        _outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(_outputDir);
        PhotoExporter.ClearClaimedExportPaths();
    }

    private string CreateScan(string name, string? directory = null)
    {
        string path = Path.Combine(directory ?? _tempDir, name);
        using Mat scan = new(50, 50, DepthType.Cv8U, 3);
        scan.SetTo(new MCvScalar(200, 200, 200));
        scan.Save(path);
        return path;
    }

    private string Touch(string name)
    {
        string path = Path.Combine(_outputDir, name);
        File.WriteAllText(path, "existing");
        return path;
    }

    private static Mat Photo()
    {
        var photo = new Mat(10, 10, DepthType.Cv8U, 3);
        photo.SetTo(new MCvScalar(0, 0, 0));
        return photo;
    }

    [Fact]
    public void ReExport_ShouldKeepUnrelatedFilesSharingThePrefix()
    {
        string scan = CreateScan("IMG.jpg");
        string[] unrelated =
        [
            Touch("IMG_2020_trip.jpg"),
            Touch("IMG_holiday.png"),
            Touch("family_IMG_1.jpg"),
            Touch("IMG_1_edited.jpg")
        ];
        using var photo = Photo();

        PhotoExporter.SavePhotos([photo], scan, customOutputFolder: _outputDir, cleanOldExports: true);

        foreach (string file in unrelated)
        {
            File.Exists(file).ShouldBeTrue($"'{Path.GetFileName(file)}' is not an export of this scan and must survive");
        }
    }

    [Fact]
    public void ReExport_ShouldReplacePreviousExportsInsteadOfAddingDuplicates()
    {
        string scan = CreateScan("scan_0001.jpg");
        Touch("scan_0001_1.jpg");
        Touch("scan_0001_1 (1).jpg");
        Touch("scan_0001_2.png");
        Touch("scan_0001_3.jpg");
        using var photo1 = Photo();
        using var photo2 = Photo();

        PhotoExporter.SavePhotos([photo1, photo2], scan, customOutputFolder: _outputDir, cleanOldExports: true);

        Directory.GetFiles(_outputDir).Select(Path.GetFileName).Order()
            .ShouldBe(["scan_0001_1.jpg", "scan_0001_2.jpg"]);
    }

    [Fact]
    public void ReExport_ShouldNotTouchOtherScansExports()
    {
        string scan = CreateScan("scan_1.jpg");
        string otherScanExport = Touch("scan_10_1.jpg");
        using var photo = Photo();

        PhotoExporter.SavePhotos([photo], scan, customOutputFolder: _outputDir, cleanOldExports: true);

        File.Exists(otherScanExport).ShouldBeTrue();
    }

    [Fact]
    public void ReExport_WithPatternWithoutOriginal_ShouldNotDeleteAnything()
    {
        // "{year}_{index:02}" names can't be attributed to one scan, so they might belong to another one.
        string scan = CreateScan("scan_0001.jpg");
        string possiblyOtherScan = Touch("1975_01.jpg");
        using var photo = Photo();

        PhotoExporter.SavePhotos([photo], scan, customOutputFolder: _outputDir,
            fileNamePattern: FileNameTemplateHelper.YearIndexPattern,
            metadata: new PhotoExportMetadata { Year = 1975 },
            cleanOldExports: true);

        File.Exists(possiblyOtherScan).ShouldBeTrue();
    }

    [Fact]
    public void ReExport_IntoScanFolderWithOriginalOnlyPattern_ShouldNeverDeleteTheSourceScan()
    {
        string scan = CreateScan("scan_0001.jpg", _outputDir);
        using var photo = Photo();

        PhotoExporter.SavePhotos([photo], scan, customOutputFolder: _outputDir,
            fileNamePattern: "{original}", cleanOldExports: true);

        File.Exists(scan).ShouldBeTrue();
    }

    [Fact]
    public void FindPreviousExports_ShouldMatchYearPatternExactly()
    {
        var metadata = new PhotoExportMetadata { Year = 1975 };
        string ours = Touch("1975_scan_0001_01.jpg");
        Touch("1980_scan_0001_01.jpg");

        var found = PhotoExporter.FindPreviousExports(_outputDir, "scan_0001", FileNameTemplateHelper.YearOriginalPattern, metadata, totalPhotos: 1);

        found.ShouldBe([ours]);
    }

    public void Dispose()
    {
        PhotoExporter.ClearClaimedExportPaths();
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }
        catch (IOException)
        {
        }
    }
}
