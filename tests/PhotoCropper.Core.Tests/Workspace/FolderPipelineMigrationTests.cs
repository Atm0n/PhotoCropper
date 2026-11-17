using PhotoCropper.Core.Export;
using PhotoCropper.Core.Workspace;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Core.Tests.Workspace;

public sealed class FolderPipelineMigrationTests : IDisposable
{
    private readonly string _workDir = TestImageFactory.CreateTempDirectory("FolderPipelineMigration");

    private string Cropped(string name) => Path.Combine(ProjectWorkspaceService.GetCroppedDirectory(_workDir), name);
    private string Final(string name) => Path.Combine(ProjectWorkspaceService.GetFinalDirectory(_workDir), name);

    private void WriteLegacySessionJson(string json)
    {
        Directory.CreateDirectory(ProjectWorkspaceService.GetCroppedDirectory(_workDir));
        File.WriteAllText(ProjectWorkspaceService.GetSessionFilePath(_workDir), json);
    }

    [Fact]
    public void LoadSession_OldProject_ShouldMoveExportsFromCroppedToFinal()
    {
        WriteLegacySessionJson("""{ "Version": "1.0", "Scans": [] }""");
        File.WriteAllText(Cropped("scan_0001_1.jpg"), "old export");

        var session = ProjectWorkspaceService.LoadSession(_workDir);

        session!.Version.ShouldBe(WorkspaceSessionState.CurrentVersion);
        File.Exists(Cropped("scan_0001_1.jpg")).ShouldBeFalse();
        File.ReadAllText(Final("scan_0001_1.jpg")).ShouldBe("old export");
    }

    [Fact]
    public void LoadSession_SessionWithoutVersion_ShouldBeTreatedAsOldAndMigrated()
    {
        WriteLegacySessionJson("""{ "Scans": [] }""");
        File.WriteAllText(Cropped("photo.jpg"), "old export");

        ProjectWorkspaceService.LoadSession(_workDir);

        File.Exists(Final("photo.jpg")).ShouldBeTrue();
    }

    [Fact]
    public void LoadSession_OldProject_ShouldUpdateExportRecordsToTheNewLocation()
    {
        File.WriteAllText(Path.Combine(_workDir, "placeholder"), "");
        Directory.CreateDirectory(ProjectWorkspaceService.GetCroppedDirectory(_workDir));
        File.WriteAllText(Cropped("scan_0001_1.jpg"), "old export");
        var legacy = new WorkspaceSessionState { Version = "1.0" };
        var entry = new WorkspaceScanEntry { RelativePath = "RawScans/scan_0001.png" };
        entry.ExportedFiles.Add(ExportedFile.FromDisk(Cropped("scan_0001_1.jpg")));
        legacy.Scans.Add(entry);
        File.WriteAllText(ProjectWorkspaceService.GetSessionFilePath(_workDir),
            System.Text.Json.JsonSerializer.Serialize(legacy));

        var session = ProjectWorkspaceService.LoadSession(_workDir);

        var record = session!.Scans[0].ExportedFiles[0];
        Path.GetFullPath(record.Path).ShouldBe(Path.GetFullPath(Final("scan_0001_1.jpg")));
        record.IsUnchangedOnDisk().ShouldBeTrue();
    }

    [Fact]
    public void LoadSession_NewProject_ShouldNeverMovePipelineFiles()
    {
        var session = WorkspaceSessionState.CreateNew();
        ProjectWorkspaceService.SaveSession(_workDir, session);
        File.WriteAllText(Cropped("scan_0001_p01.png"), "pipeline photo");
        File.WriteAllText(Cropped("import_0001.png"), "imported photo");

        ProjectWorkspaceService.LoadSession(_workDir);

        File.Exists(Cropped("scan_0001_p01.png")).ShouldBeTrue();
        File.Exists(Cropped("import_0001.png")).ShouldBeTrue();
    }

    [Fact]
    public void SaveSession_ShouldStampCurrentVersionEvenOnASessionCreatedWithNew()
    {
        // GUI code creates sessions with "new WorkspaceSessionState()"; saving must not leave them looking old.
        ProjectWorkspaceService.SaveSession(_workDir, new WorkspaceSessionState());
        File.WriteAllText(Cropped("scan_0001_p01.png"), "pipeline photo");
        File.WriteAllText(Cropped("not_pipeline_named.jpg"), "user file");

        ProjectWorkspaceService.LoadSession(_workDir)!.Version.ShouldBe(WorkspaceSessionState.CurrentVersion);

        File.Exists(Cropped("not_pipeline_named.jpg")).ShouldBeTrue("a current-version project must not be migrated again");
    }

    [Fact]
    public void ReconstructSession_WithoutSessionFile_ShouldMigrateOldExports()
    {
        ProjectWorkspaceService.InitializeWorkspace(_workDir);
        File.WriteAllText(Path.Combine(ProjectWorkspaceService.GetRawScansDirectory(_workDir), "scan_0001.png"), "scan");
        File.WriteAllText(Cropped("scan_0001_1.jpg"), "old export");

        var session = ProjectWorkspaceService.ReconstructSessionFromRawFiles(_workDir);

        File.Exists(Final("scan_0001_1.jpg")).ShouldBeTrue();
        session.Scans[0].IsProcessed.ShouldBeTrue();
        session.Scans[0].ExtractedPhotoCount.ShouldBe(1);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workDir)) Directory.Delete(_workDir, true);
        }
        catch (IOException)
        {
        }
    }
}
