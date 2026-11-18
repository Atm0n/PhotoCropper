using PhotoCropper.Cli.Models;
using PhotoCropper.Cli.Parsing;
using PhotoCropper.Cli.Services;
using PhotoCropper.Core.Workspace;
using PhotoCropper.TestHelpers;
using Spectre.Console.Testing;

namespace PhotoCropper.Cli.Tests.Cli;

[Collection(nameof(ConsoleProgressGroup))]
public sealed class ProjectProcessorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _inbox;
    private readonly string _project;

    public ProjectProcessorTests()
    {
        _tempDir = TestImageFactory.CreateTempDirectory("ProjectProcessorTests");
        _inbox = Path.Combine(_tempDir, "inbox");
        Directory.CreateDirectory(_inbox);
        TestImageFactory.CreateStandardTwoPhotoScan(Path.Combine(_inbox, "grandma_01.jpg"));
        _project = Path.Combine(_tempDir, "Album");
    }

    private static CliOptions Options(PipelineStage stage = PipelineStage.All, params string[] inputs)
    {
        var options = new CliOptions { Stage = stage, Format = "PNG", Tolerance = 30, MinAreaFactor = 0.01, NonInteractive = true, Threads = 1 };
        options.Inputs.AddRange(inputs);
        return options;
    }

    [Fact]
    public void Run_NewProject_ShouldAddTheScansAndWriteEveryStage()
    {
        using var console = new TestConsole();

        var result = ProjectProcessor.Run(Options(PipelineStage.All, _inbox), _project, console);

        result.Added.ShouldBe(1);
        result.Scans.Single().Outcome.ShouldBe(ProjectProcessor.ScanOutcome.Detected);
        result.Photos.ShouldBe(2);
        result.FinalsWritten.ShouldBe(2);

        Directory.GetFiles(ProjectWorkspaceService.GetRawScansDirectory(_project)).Length.ShouldBe(1);
        Directory.GetFiles(ProjectWorkspaceService.GetCroppedDirectory(_project), "*.png").Length.ShouldBe(2);
        Directory.GetFiles(ProjectWorkspaceService.GetFinalDirectory(_project)).Length.ShouldBe(2);

        // The app opens it as it left it: boxes saved, photos recorded.
        var session = ProjectWorkspaceService.LoadSession(_project)!;
        var entry = session.Scans.Single();
        entry.OriginalFileName.ShouldBe("grandma_01.jpg");
        entry.HasSavedCrops.ShouldBeTrue();
        entry.FinalCrops.Count.ShouldBe(2);
        session.Photos.Count.ShouldBe(2);
    }

    [Fact]
    public void Run_Again_ShouldKeepSavedBoxesAndWriteNothingNew()
    {
        using var console = new TestConsole();
        ProjectProcessor.Run(Options(PipelineStage.All, _inbox), _project, console);

        string before = CroppedFingerprint();

        var result = ProjectProcessor.Run(Options(), _project, console);

        // Restoring the saved boxes gives the very same cropped files, so nothing is rewritten or re-rendered.
        CroppedFingerprint().ShouldBe(before);

        result.Added.ShouldBe(0);
        result.Scans.Single().Outcome.ShouldBe(ProjectProcessor.ScanOutcome.Saved);
        result.FinalsWritten.ShouldBe(0);
        Directory.GetFiles(ProjectWorkspaceService.GetFinalDirectory(_project)).Length.ShouldBe(2);
    }

    private string CroppedFingerprint() => string.Join(',', Directory.GetFiles(ProjectWorkspaceService.GetCroppedDirectory(_project))
        .Order(StringComparer.Ordinal)
        .Select(f => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f)))));

    [Fact]
    public void Run_ShouldUseBoxesEditedInTheApp()
    {
        using var console = new TestConsole();
        ProjectProcessor.Run(Options(PipelineStage.Crop, _inbox), _project, console);

        // In the app, one of the two photos is deleted (autosave writes the remaining box).
        var session = ProjectWorkspaceService.LoadSession(_project)!;
        var entry = session.Scans.Single();
        entry.SetFinalCrops([entry.FinalCrops[0]]);
        ProjectWorkspaceService.SaveSession(_project, session);

        var result = ProjectProcessor.Run(Options(), _project, console);

        result.Scans.Single().Photos.ShouldBe(1);
        Directory.GetFiles(ProjectWorkspaceService.GetCroppedDirectory(_project), "*.png").Length.ShouldBe(1);
        Directory.GetFiles(ProjectWorkspaceService.GetFinalDirectory(_project)).Length.ShouldBe(1);
    }

    [Fact]
    public void Run_ScanWithNothingFound_ShouldStayFlaggedAndBeTriedAgain()
    {
        string blank = Path.Combine(_tempDir, "blank");
        Directory.CreateDirectory(blank);
        using (var empty = new Emgu.CV.Mat(600, 600, Emgu.CV.CvEnum.DepthType.Cv8U, 3))
        {
            empty.SetTo(new Emgu.CV.Structure.MCvScalar(255, 255, 255));
            Emgu.CV.CvInvoke.Imwrite(Path.Combine(blank, "empty.png"), empty);
        }
        using var console = new TestConsole();

        var first = ProjectProcessor.Run(Options(PipelineStage.Crop, blank), _project, console);
        var second = ProjectProcessor.Run(Options(PipelineStage.Crop), _project, console);

        first.Scans.Single().Outcome.ShouldBe(ProjectProcessor.ScanOutcome.Flagged);
        second.Scans.Single().Outcome.ShouldBe(ProjectProcessor.ScanOutcome.Flagged);
        ProjectWorkspaceService.LoadSession(_project)!.Scans.Single().HasSavedCrops.ShouldBeFalse();
    }

    [Fact]
    public void Run_CropStage_ShouldNotWriteFinalPhotos()
    {
        using var console = new TestConsole();

        var result = ProjectProcessor.Run(Options(PipelineStage.Crop, _inbox), _project, console);

        result.Photos.ShouldBe(2);
        result.FinalsWritten.ShouldBe(0);
        Directory.Exists(ProjectWorkspaceService.GetFinalDirectory(_project)).ShouldBeTrue();
        Directory.GetFiles(ProjectWorkspaceService.GetFinalDirectory(_project)).ShouldBeEmpty();
    }

    [Fact]
    public void Run_RestoreStage_ShouldOnlyRenderMissingFinals()
    {
        using var console = new TestConsole();
        ProjectProcessor.Run(Options(PipelineStage.Crop, _inbox), _project, console);

        var result = ProjectProcessor.Run(Options(PipelineStage.Restore), _project, console);

        result.Scans.ShouldBeEmpty();
        result.FinalsWritten.ShouldBe(2);
    }

    [Fact]
    public void Parse_Project_ShouldSetTheFolderAndKeepInputs()
    {
        var options = CommandLineParser.Parse(["--project", _project, _inbox, "--stage", "crop"]);

        options.ProjectDirectory.ShouldBe(_project);
        options.Inputs.ShouldBe([_inbox]);
        options.Stage.ShouldBe(PipelineStage.Crop);
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
