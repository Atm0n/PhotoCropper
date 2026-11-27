using OpenCvSharp;
using PhotoCropper.Core.Export;
using PhotoCropper.Core.Models;
using PhotoCropper.Core.Workspace;
using PhotoCropper.TestHelpers;
using System.Text;

namespace PhotoCropper.Core.Tests.Export;

public sealed class RestoreStageTests : IDisposable
{
    private readonly string _tempDir = TestImageFactory.CreateTempDirectory("RestoreStageTests");

    private string CreatePhoto(string name)
    {
        string path = Path.Combine(_tempDir, name);
        using var mat = TestImageFactory.CreateFadedPhoto(120, 80);
        Cv2.ImWrite(path, mat);
        return path;
    }

    [Fact]
    public void RestoreFileShouldKeepTheNameAndWriteToTheOutputFolder()
    {
        string source = CreatePhoto("grandpa.png");
        string output = Path.Combine(_tempDir, "restored");

        var written = RestoreStage.RestoreFile(source, output, new RestorationSettings { RestoreColors = true });

        Path.GetFileName(written.Path).ShouldBe("grandpa.jpg");
        Path.GetDirectoryName(written.Path).ShouldBe(output);
    }

    [Fact]
    public void RestoreFileWithRestorationShouldChangeThePixels()
    {
        string source = CreatePhoto("faded.png");
        string output = Path.Combine(_tempDir, "out");

        var plain = RestoreStage.RestoreFile(source, output, RestorationSettings.None, "PNG", fileNamePattern: "{original}_plain");
        var restored = RestoreStage.RestoreFile(source, output, new RestorationSettings { RestoreColors = true }, "PNG", fileNamePattern: "{original}_restored");

        using var a = RestoreStage.ReadImage(plain.Path);
        using var b = RestoreStage.ReadImage(restored.Path);
        Cv2.Norm(a, b, NormTypes.L1).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void RestoreFileIntoTheSourceFolderShouldNotOverwriteTheSource()
    {
        string source = CreatePhoto("photo.jpg");
        byte[] original = File.ReadAllBytes(source);

        var written = RestoreStage.RestoreFile(source, _tempDir, RestorationSettings.None);

        written.Path.ShouldNotBe(source);
        File.ReadAllBytes(source).ShouldBe(original);
    }

    [Fact]
    public void RestoreFileWithMetadataShouldEmbedIt()
    {
        string source = CreatePhoto("photo.png");

        var written = RestoreStage.RestoreFile(source, Path.Combine(_tempDir, "meta"), RestorationSettings.None,
            metadata: new PhotoExportMetadata { Photographer = "Josep Martí" });

        File.ReadAllBytes(written.Path).AsSpan().IndexOf(Encoding.UTF8.GetBytes("Josep Martí")).ShouldBeGreaterThanOrEqualTo(0);
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
