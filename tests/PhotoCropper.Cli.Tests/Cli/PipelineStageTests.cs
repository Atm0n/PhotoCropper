using PhotoCropper.Cli.Models;
using PhotoCropper.Cli.Parsing;
using PhotoCropper.Cli.Services;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Cli.Tests.Cli;

[Collection(nameof(ConsoleProgressGroup))]
public sealed class PipelineStageTests : IDisposable
{
    private readonly string _tempDir = TestImageFactory.CreateTempDirectory("PipelineStageTests");

    // The enum is internal, so the expected stage is passed by name.
    [Theory]
    [InlineData("crop", nameof(PipelineStage.Crop))]
    [InlineData("RESTORE", nameof(PipelineStage.Restore))]
    [InlineData("all", nameof(PipelineStage.All))]
    public void Parse_Stage_ShouldSetTheStage(string value, string expected)
    {
        CommandLineParser.Parse(["--stage", value]).Stage.ToString().ShouldBe(expected);
    }

    [Fact]
    public void Parse_UnknownStage_ShouldFallBackToAll()
    {
        CommandLineParser.Parse(["--stage", "polish"]).Stage.ShouldBe(PipelineStage.All);
    }

    [Fact]
    public void Parse_Pattern_ShouldBeMarkedAsSpecified()
    {
        CommandLineParser.Parse(["-p", "{original}"]).FileNamePatternSpecified.ShouldBeTrue();
        CommandLineParser.Parse([]).FileNamePatternSpecified.ShouldBeFalse();
    }

    [Fact]
    public void CropStage_ShouldWriteLosslessPngsWithoutRestoration()
    {
        string scan = Path.Combine(_tempDir, "scan01.jpg");
        TestImageFactory.CreateStandardTwoPhotoScan(scan);
        string output = Path.Combine(_tempDir, "cropped");
        var options = new CliOptions
        {
            Stage = PipelineStage.Crop,
            OutputDirectory = output,
            Tolerance = 30,
            MinAreaFactor = 0.01,
            NonInteractive = true,
            Threads = 1
        };

        int exit = BatchProcessor.Execute(options, [scan]);

        exit.ShouldBe(0);
        options.RestoreColors.ShouldBeFalse();
        Directory.GetFiles(output, "*.png").Length.ShouldBeGreaterThanOrEqualTo(2);
        Directory.GetFiles(output, "*.jpg").ShouldBeEmpty();
    }

    [Fact]
    public void RestoreStage_ShouldRestoreEachPhotoKeepingItsName()
    {
        string input = Directory.CreateDirectory(Path.Combine(_tempDir, "photos")).FullName;
        foreach (string name in new[] { "a.png", "b.png" })
        {
            using var photo = TestImageFactory.CreateFadedPhoto(100, 80);
            OpenCvSharp.Cv2.ImWrite(Path.Combine(input, name), photo);
        }
        var options = new CliOptions { Stage = PipelineStage.Restore, NonInteractive = true, Threads = 1 };
        string[] files = [.. Directory.GetFiles(input)];

        int exit = RestoreProcessor.Execute(options, files);

        exit.ShouldBe(0);
        string output = Path.Combine(input, RestoreProcessor.DefaultOutputFolderName);
        Directory.GetFiles(output).Select(Path.GetFileName).Order().ShouldBe(["a.jpg", "b.jpg"]);
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
