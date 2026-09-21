using PhotoCropper.Core.IO;

namespace PhotoCropper.Core.Tests.IO;

public sealed class ImageFileCollectorTests : IDisposable
{
    private readonly string _tempDir;

    public ImageFileCollectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ImageFileCollectorTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Theory]
    [InlineData("photo.jpg", true)]
    [InlineData("photo.JPEG", true)]
    [InlineData("scan.png", true)]
    [InlineData("doc.bmp", true)]
    [InlineData("archive.tif", true)]
    [InlineData("archive.TIFF", true)]
    [InlineData("image.webp", true)]
    [InlineData("document.pdf", false)]
    [InlineData("data.txt", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupportedImageFile_ShouldMatchExpectedExtensions(string? fileName, bool expected)
    {
        ImageFileCollector.IsSupportedImageFile(fileName).ShouldBe(expected);
    }

    [Fact]
    public void CollectFiles_ShouldFilterAndOrderImagesRecursively()
    {
        string subDir = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(subDir);

        string f1 = Path.Combine(_tempDir, "b_scan.png");
        string f2 = Path.Combine(_tempDir, "a_scan.jpg");
        string f3 = Path.Combine(subDir, "c_scan.webp");
        string txt = Path.Combine(_tempDir, "readme.txt");

        File.WriteAllText(f1, "dummy");
        File.WriteAllText(f2, "dummy");
        File.WriteAllText(f3, "dummy");
        File.WriteAllText(txt, "dummy");

        var collected = ImageFileCollector.CollectFiles([_tempDir], recursive: true);

        collected.Count.ShouldBe(3);
        collected.ShouldNotContain(txt);
        collected[0].ShouldBe(f2); // a_scan.jpg
        collected[1].ShouldBe(f1); // b_scan.png
        collected[2].ShouldBe(f3); // c_scan.webp
    }

    [Fact]
    public void CollectFiles_NonRecursive_ShouldOnlyCollectTopLevel()
    {
        string subDir = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(subDir);

        string topImage = Path.Combine(_tempDir, "top.jpg");
        string subImage = Path.Combine(subDir, "sub.jpg");

        File.WriteAllText(topImage, "dummy");
        File.WriteAllText(subImage, "dummy");

        var collected = ImageFileCollector.CollectFiles([_tempDir], recursive: false);

        collected.Count.ShouldBe(1);
        collected.ShouldContain(topImage);
        collected.ShouldNotContain(subImage);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
        }
    }
}
