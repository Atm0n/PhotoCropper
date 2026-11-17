using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Core.Models;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.ViewModels;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Gui.Tests.ViewModels;

public sealed class StageViewModelTests : IDisposable
{
    private readonly string _workDir;
    private readonly FakeHost _host;

    public StageViewModelTests()
    {
        TestAppBuilder.EnsureInitialized();
        _workDir = TestImageFactory.CreateTempDirectory("StageViewModelTests");
        ProjectWorkspaceService.InitializeWorkspace(_workDir);
        _host = new FakeHost(_workDir, WorkspaceSessionState.CreateNew());
    }

    private ProjectPhoto AddCroppedPhoto(string scanName, bool restoreColors = false)
    {
        string scanPath = Path.Combine(ProjectWorkspaceService.GetRawScansDirectory(_workDir), scanName);
        using (var scan = new Mat(200, 200, DepthType.Cv8U, 3)) { scan.SetTo(new MCvScalar(230, 230, 230)); scan.Save(scanPath); }
        var entry = new WorkspaceScanEntry { RelativePath = Path.GetRelativePath(_workDir, scanPath) };
        _host.Workspace!.Scans.Add(entry);

        using var photo = new Mat(40, 60, DepthType.Cv8U, 3);
        photo.SetTo(new MCvScalar(90, 120, 150));
        var crop = new WorkspaceCropData { CenterX = 50, CenterY = 50, Width = 60, Height = 40 };
        return PhotoPipelineService.SaveCroppedPhotos(_workDir, _host.Workspace, entry, [new CroppedPhotoInput(photo, crop)],
            new RestorationSettings { RestoreColors = restoreColors })[0];
    }

    [Fact]
    public void FinishStage_Refresh_ShouldListCroppedPhotos()
    {
        AddCroppedPhoto("scan_0001.png");
        AddCroppedPhoto("scan_0002.png");
        var vm = new FinishStageViewModel(_host);

        vm.Refresh();

        vm.HasProject.ShouldBeTrue();
        vm.Photos.Select(p => p.Title).ShouldBe(["scan_0001_p01", "scan_0002_p01"]);
        vm.ExportCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void FinishStage_WithoutProject_ShouldShowNoProject()
    {
        var vm = new FinishStageViewModel(new FakeHost(null, null));

        vm.Refresh();

        vm.HasProject.ShouldBeFalse();
        vm.Photos.ShouldBeEmpty();
        vm.ExportCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task FinishStage_Export_ShouldWriteFinalsAndClearStaleness()
    {
        AddCroppedPhoto("scan_0001.png");
        var vm = new FinishStageViewModel(_host);
        vm.Refresh();

        await vm.ExportCommand.ExecuteAsync(null);

        vm.Photos[0].IsStale.ShouldBeFalse();
        File.Exists(vm.Photos[0].Photo.Final!.GetFullPath(_workDir)).ShouldBeTrue();
        vm.ExportCommand.CanExecute(null).ShouldBeFalse();
        _host.Saves.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task FinishStage_ChangingRestoration_ShouldMarkStaleAndSaveImmediately()
    {
        AddCroppedPhoto("scan_0001.png");
        var vm = new FinishStageViewModel(_host);
        vm.Refresh();
        await vm.ExportCommand.ExecuteAsync(null);
        int savesBefore = _host.Saves;

        vm.Photos[0].RestoreColors = true;

        vm.Photos[0].Photo.Restoration.RestoreColors.ShouldBeTrue();
        vm.Photos[0].IsStale.ShouldBeTrue();
        _host.Saves.ShouldBe(savesBefore + 1);
        vm.ExportCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void FinishStage_RestoreAll_ShouldSetEveryPhoto()
    {
        AddCroppedPhoto("scan_0001.png");
        AddCroppedPhoto("scan_0002.png");
        var vm = new FinishStageViewModel(_host);
        vm.Refresh();

        vm.RestoreAllCommand.Execute(null);

        vm.Photos.ShouldAllBe(p => p.RestoreColors);
    }

    [Fact]
    public void ScanStage_Refresh_ShouldShowCountsFromSavedCropsAndFlagEmptyScans()
    {
        AddCroppedPhoto("scan_0001.png");
        string emptyScan = Path.Combine(ProjectWorkspaceService.GetRawScansDirectory(_workDir), "scan_0002.png");
        using (var scan = new Mat(200, 200, DepthType.Cv8U, 3)) { scan.SetTo(new MCvScalar(230, 230, 230)); scan.Save(emptyScan); }
        _host.ScanItems.Add(new ScanSessionItem(Path.Combine(ProjectWorkspaceService.GetRawScansDirectory(_workDir), "scan_0001.png"), new DetectionOptions()));
        _host.ScanItems.Add(new ScanSessionItem(emptyScan, new DetectionOptions()));
        var vm = new ScanStageViewModel(_host);

        vm.Refresh();

        vm.Scans.Count.ShouldBe(2);
        vm.Scans[0].PhotosText.ShouldContain("1");
        vm.Scans[0].IsFlagged.ShouldBeFalse();
        vm.Scans[1].IsFlagged.ShouldBeFalse("a scan that was never checked is not known to be empty");
    }

    [Fact]
    public void ScanStage_OpenScan_ShouldAskTheHostToOpenItInCrops()
    {
        var vm = new ScanStageViewModel(_host);

        vm.OpenScanCommand.Execute(new ScanRowViewModel(3, "scan.png", "2 photos", false));

        _host.OpenedScan.ShouldBe(3);
    }

    public void Dispose()
    {
        foreach (var item in _host.ScanItems) item.Dispose();
        try
        {
            if (Directory.Exists(_workDir)) Directory.Delete(_workDir, true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class FakeHost(string? workDir, WorkspaceSessionState? workspace) : IStageHost
    {
        public string? WorkDirectory { get; } = workDir;
        public WorkspaceSessionState? Workspace { get; } = workspace;
        public List<ScanSessionItem> ScanItems { get; } = [];
        public IReadOnlyList<ScanSessionItem> Scans => ScanItems;
        public RestorationSettings DefaultRestoration => RestorationSettings.None;
        public int Saves { get; private set; }
        public int? OpenedScan { get; private set; }

        public Task ScanAsync() => Task.CompletedTask;
        public Task ImportScansAsync() => Task.CompletedTask;
        public void OpenScanInCrops(int scanIndex) => OpenedScan = scanIndex;
        public Task<IReadOnlyList<string>> PickPhotosToImportAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public FinalRenderOptions GetRenderOptions() => new();
        public async Task<bool> RunBusyAsync(string message, Func<Task> work) { await work(); return true; }
        public void SaveWorkspace() => Saves++;
        public void ShowStatus(string message) { }
        public void OpenFolder(string path) { }
    }
}
