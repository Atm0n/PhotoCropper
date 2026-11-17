using PhotoCropper.Core.Extraction;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Core.Tests.Extraction;

public sealed class FaceModelFileTests : IDisposable
{
    private readonly string _tempDir = TestImageFactory.CreateTempDirectory("FaceModelFileTests");

    private static byte[] FakeModel(int size = 256 * 1024)
    {
        var bytes = new byte[size];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 31 % 251);
        return bytes;
    }

    [Fact]
    public void WriteModelAtomically_ShouldWriteCompleteFileAndLeaveNoTempFiles()
    {
        byte[] model = FakeModel();
        string target = Path.Combine(_tempDir, "model.onnx");

        using (var source = new MemoryStream(model))
        {
            FaceOrientationService.WriteModelAtomically(source, target);
        }

        File.ReadAllBytes(target).ShouldBe(model);
        Directory.GetFiles(_tempDir).ShouldBe([target]);
    }

    [Fact]
    public void WriteModelAtomically_ConcurrentWriters_ShouldEndWithACompleteModel()
    {
        // Simulates the GUI and CLI extracting the model at the same moment.
        byte[] model = FakeModel();
        string target = Path.Combine(_tempDir, "model.onnx");

        Parallel.For(0, 8, _ =>
        {
            using var source = new MemoryStream(model);
            FaceOrientationService.WriteModelAtomically(source, target);
        });

        File.ReadAllBytes(target).ShouldBe(model);
        Directory.GetFiles(_tempDir, "*.tmp").ShouldBeEmpty();
    }

    [Fact]
    public void WriteModelAtomically_ShouldReplaceATruncatedModel()
    {
        byte[] model = FakeModel();
        string target = Path.Combine(_tempDir, "model.onnx");
        File.WriteAllBytes(target, model[..1000]);

        using (var source = new MemoryStream(model))
        {
            FaceOrientationService.WriteModelAtomically(source, target);
        }

        File.ReadAllBytes(target).ShouldBe(model);
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
