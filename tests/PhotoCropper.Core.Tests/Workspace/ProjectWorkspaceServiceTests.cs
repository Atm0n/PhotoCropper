using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Core.Workspace;
using PhotoCropper.TestHelpers;
using System.Drawing;

namespace PhotoCropper.Core.Tests.Workspace;

public sealed class ProjectWorkspaceServiceTests : IDisposable
{
    private readonly string _tempDir;

    public ProjectWorkspaceServiceTests()
    {
        _tempDir = TestImageFactory.CreateTempDirectory("WorkspaceTests");
    }

    [Fact]
    public void InitializeWorkspace_ShouldCreateDirectories()
    {
        string workDir = Path.Combine(_tempDir, "Project1");
        ProjectWorkspaceService.InitializeWorkspace(workDir);

        Directory.Exists(workDir).ShouldBeTrue();
        Directory.Exists(ProjectWorkspaceService.GetRawScansDirectory(workDir)).ShouldBeTrue();
        Directory.Exists(ProjectWorkspaceService.GetCroppedDirectory(workDir)).ShouldBeTrue();
    }

    [Fact]
    public void GetNextScanFileName_EmptyDirectory_ShouldReturnScan0001()
    {
        string workDir = Path.Combine(_tempDir, "Project2");
        string next = ProjectWorkspaceService.GetNextScanFileName(workDir);

        next.ShouldBe("scan_0001.png");
    }

    [Fact]
    public void GetNextScanFileName_ExistingFiles_ShouldIncrement()
    {
        string workDir = Path.Combine(_tempDir, "Project3");
        ProjectWorkspaceService.InitializeWorkspace(workDir);
        string rawDir = ProjectWorkspaceService.GetRawScansDirectory(workDir);

        File.WriteAllText(Path.Combine(rawDir, "scan_0001.png"), "");
        File.WriteAllText(Path.Combine(rawDir, "scan_0002.png"), "");
        File.WriteAllText(Path.Combine(rawDir, "scan_0007.png"), "");

        string next = ProjectWorkspaceService.GetNextScanFileName(workDir);
        next.ShouldBe("scan_0008.png");
    }

    [Fact]
    public void StageRawScanFromMat_ShouldWritePngFile()
    {
        string workDir = Path.Combine(_tempDir, "Project4");
        using Mat mat = new(200, 200, DepthType.Cv8U, 3);
        mat.SetTo(new MCvScalar(128, 128, 128));

        string stagedPath = ProjectWorkspaceService.StageRawScanFromMat(workDir, mat);

        File.Exists(stagedPath).ShouldBeTrue();
        Path.GetFileName(stagedPath).ShouldBe("scan_0001.png");
    }

    [Fact]
    public void StageRawScanFromFile_ShouldCopyFile()
    {
        string workDir = Path.Combine(_tempDir, "Project5");
        string sourceFile = Path.Combine(_tempDir, "source.jpg");
        using (Mat mat = new(100, 100, DepthType.Cv8U, 3))
        {
            mat.SetTo(new MCvScalar(255, 255, 255));
            mat.Save(sourceFile);
        }

        string stagedPath = ProjectWorkspaceService.StageRawScanFromFile(workDir, sourceFile);

        File.Exists(stagedPath).ShouldBeTrue();
        Path.GetFileName(stagedPath).ShouldBe("scan_0001.jpg");
    }

    [Fact]
    public void SaveSession_And_LoadSession_ShouldRoundtrip()
    {
        string workDir = Path.Combine(_tempDir, "Project6");
        var state = new WorkspaceSessionState();
        var entry = new WorkspaceScanEntry
        {
            OriginalFileName = "scan_0001.png",
            RelativePath = "RawScans/scan_0001.png",
            IsProcessed = true,
            ExtractedPhotoCount = 2
        };
        entry.Rotations.Add(90);
        entry.ManualCrops.Add(new Rectangle(10, 10, 200, 200));
        state.Scans.Add(entry);

        ProjectWorkspaceService.SaveSession(workDir, state);

        var loaded = ProjectWorkspaceService.LoadSession(workDir);
        loaded.ShouldNotBeNull();
        loaded.Scans.Count.ShouldBe(1);
        loaded.Scans[0].OriginalFileName.ShouldBe("scan_0001.png");
        loaded.Scans[0].Rotations.ShouldContain(90);
        loaded.Scans[0].ExtractedPhotoCount.ShouldBe(2);
    }

    [Fact]
    public void HasRecoverableSession_WithSessionFile_ShouldReturnTrue()
    {
        string workDir = Path.Combine(_tempDir, "Project7");
        var state = new WorkspaceSessionState();
        state.Scans.Add(new WorkspaceScanEntry { RelativePath = "RawScans/scan_0001.png" });
        ProjectWorkspaceService.SaveSession(workDir, state);

        ProjectWorkspaceService.HasRecoverableSession(workDir).ShouldBeTrue();
    }

    [Fact]
    public void HasRecoverableSession_WithEmptyDir_ShouldReturnFalse()
    {
        string workDir = Path.Combine(_tempDir, "Project8");
        ProjectWorkspaceService.InitializeWorkspace(workDir);

        ProjectWorkspaceService.HasRecoverableSession(workDir).ShouldBeFalse();
    }

    [Fact]
    public void ReconstructSessionFromRawFiles_ShouldIndexExistingFiles()
    {
        string workDir = Path.Combine(_tempDir, "Project9");
        ProjectWorkspaceService.InitializeWorkspace(workDir);
        string rawDir = ProjectWorkspaceService.GetRawScansDirectory(workDir);

        File.WriteAllText(Path.Combine(rawDir, "scan_0001.png"), "");
        File.WriteAllText(Path.Combine(rawDir, "scan_0002.png"), "");

        var state = ProjectWorkspaceService.ReconstructSessionFromRawFiles(workDir);

        state.Scans.Count.ShouldBe(2);
        state.Scans[0].OriginalFileName.ShouldBe("scan_0001.png");
        state.Scans[1].OriginalFileName.ShouldBe("scan_0002.png");
    }

    [Fact]
    public void ReconstructSessionFromRawFiles_WithExistingCroppedPhotos_ShouldMarkProcessed()
    {
        string workDir = Path.Combine(_tempDir, "Project9B");
        ProjectWorkspaceService.InitializeWorkspace(workDir);
        string rawDir = ProjectWorkspaceService.GetRawScansDirectory(workDir);
        string croppedDir = ProjectWorkspaceService.GetCroppedDirectory(workDir);

        File.WriteAllText(Path.Combine(rawDir, "scan_0001.png"), "");
        File.WriteAllText(Path.Combine(rawDir, "scan_0002.png"), "");

        File.WriteAllText(Path.Combine(croppedDir, "scan_0001_1.jpg"), "");
        File.WriteAllText(Path.Combine(croppedDir, "scan_0001_2.jpg"), "");

        var state = ProjectWorkspaceService.ReconstructSessionFromRawFiles(workDir);

        state.Scans.Count.ShouldBe(2);
        state.Scans[0].OriginalFileName.ShouldBe("scan_0001.png");
        state.Scans[0].IsProcessed.ShouldBeTrue();
        state.Scans[0].ExtractedPhotoCount.ShouldBe(2);

        state.Scans[1].OriginalFileName.ShouldBe("scan_0002.png");
        state.Scans[1].IsProcessed.ShouldBeFalse();
        state.Scans[1].ExtractedPhotoCount.ShouldBe(0);
    }

    [Fact]
    public void DeleteScan_ShouldRemoveFileAndSessionEntry()
    {
        string workDir = Path.Combine(_tempDir, "Project10");
        ProjectWorkspaceService.InitializeWorkspace(workDir);
        string rawDir = ProjectWorkspaceService.GetRawScansDirectory(workDir);
        string file1 = Path.Combine(rawDir, "scan_0001.png");
        string file2 = Path.Combine(rawDir, "scan_0002.png");
        File.WriteAllText(file1, "dummy");
        File.WriteAllText(file2, "dummy");

        var session = new WorkspaceSessionState();
        session.Scans.Add(new WorkspaceScanEntry { RelativePath = "RawScans/scan_0001.png", OriginalFileName = "scan_0001.png" });
        session.Scans.Add(new WorkspaceScanEntry { RelativePath = "RawScans/scan_0002.png", OriginalFileName = "scan_0002.png" });
        ProjectWorkspaceService.SaveSession(workDir, session);

        bool deleted = ProjectWorkspaceService.DeleteScan(workDir, file1, session);
        deleted.ShouldBeTrue();
        File.Exists(file1).ShouldBeFalse();
        File.Exists(file2).ShouldBeTrue();

        session.Scans.Count.ShouldBe(1);
        session.Scans[0].OriginalFileName.ShouldBe("scan_0002.png");

        var loaded = ProjectWorkspaceService.LoadSession(workDir);
        loaded.ShouldNotBeNull();
        loaded.Scans.Count.ShouldBe(1);
        loaded.Scans[0].OriginalFileName.ShouldBe("scan_0002.png");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
