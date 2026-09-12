using PhotoCropper.Cli.Models;
using PhotoCropper.Cli.Services;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Cli.Tests.Cli;

public sealed class BatchProcessorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _scanFile;

    public BatchProcessorTests()
    {
        _tempDir = TestImageFactory.CreateTempDirectory("BatchProcessorTests");
        _scanFile = Path.Combine(_tempDir, "scan01.jpg");
        TestImageFactory.CreateStandardTwoPhotoScan(_scanFile);
    }

    [Fact]
    public void Execute_NullOptions_ShouldThrowArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => BatchProcessor.Execute(null!, [_scanFile]));
    }

    [Fact]
    public void Execute_NullFiles_ShouldThrowArgumentNullException()
    {
        var options = new CliOptions();
        options.Inputs.Add(_scanFile);
        Should.Throw<ArgumentNullException>(() => BatchProcessor.Execute(options, null!));
    }

    [Fact]
    public void Execute_StandardScan_ShouldExtractAndReturnSuccess()
    {
        string outputDir = Path.Combine(_tempDir, "output");
        var options = new CliOptions
        {
            OutputDirectory = outputDir,
            Format = "PNG",
            Tolerance = 30,
            MinAreaFactor = 0.01,
            NonInteractive = true,
            Threads = 1
        };
        options.Inputs.Add(_scanFile);

        int exitCode = BatchProcessor.Execute(options, [_scanFile]);

        exitCode.ShouldBe(0);
        Directory.Exists(outputDir).ShouldBeTrue();
        var files = Directory.GetFiles(outputDir, "*.png");
        files.Length.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Execute_EmptyScanList_ShouldReturnSuccess()
    {
        var options = new CliOptions
        {
            NonInteractive = true
        };
        options.Inputs.Add(_tempDir);

        int exitCode = BatchProcessor.Execute(options, []);

        exitCode.ShouldBe(0);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
