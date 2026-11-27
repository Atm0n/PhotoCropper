using OpenCvSharp;
using PhotoCropper.Core.Export;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Core.Tests.Export;

public sealed class ExportPathClaimTests : IDisposable
{
    private readonly string _tempDir = TestImageFactory.CreateTempDirectory("ExportPathClaimTests");

    [Fact]
    public void SavePhotosShouldReleaseClaimsOnceFilesAreWritten()
    {
        string scan = Path.Combine(_tempDir, "claim_scan.jpg");
        File.WriteAllBytes(scan, [0xFF, 0xD8, 0xFF, 0xD9]);
        string outputDir = Path.Combine(_tempDir, "out");
        using Mat photo = new(20, 20, MatType.CV_8UC3);
        photo.SetTo(new Scalar(10, 20, 30));

        var written = PhotoExporter.SavePhotos([photo], scan, customOutputFolder: outputDir);
        File.Delete(written[0].Path);

        // Without releasing, the stale claim would push the next export of this name to "(1)" forever.
        string next = PhotoExporter.ResolveUniqueExportPath(written[0].Path);
        try
        {
            next.ShouldBe(written[0].Path);
        }
        finally
        {
            PhotoExporter.ReleaseExportPath(next);
        }
    }

    [Fact]
    public void ResolveUniqueExportPathAfterReleaseShouldHandOutTheSameNameAgain()
    {
        string target = Path.Combine(_tempDir, "released.jpg");

        string first = PhotoExporter.ResolveUniqueExportPath(target);
        PhotoExporter.ReleaseExportPath(first);
        string second = PhotoExporter.ResolveUniqueExportPath(target);
        PhotoExporter.ReleaseExportPath(second);

        second.ShouldBe(first);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }
        catch (IOException)
        {
        }
    }
}
