using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;
using PhotoCropper.Gui.ViewModels;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Gui.Tests.ViewModels;

public sealed class StartViewModelTests : IDisposable
{
    private readonly string _root;
    private readonly FakeHost _host;

    public StartViewModelTests()
    {
        TestAppBuilder.EnsureInitialized();
        _root = TestImageFactory.CreateTempDirectory("StartViewModelTests");
        _host = new FakeHost { DefaultProjectsFolder = _root };
    }

    [Fact]
    public async Task CreateShouldMakeTheFolderAndOpenIt()
    {
        var vm = new StartViewModel(_host);
        vm.Open();
        vm.NewProjectCommand.Execute(null);
        vm.IsCreating.ShouldBeTrue();
        vm.NewLocation.ShouldBe(_root);

        vm.NewName = "  Grandma's album  ";
        await vm.CreateCommand.ExecuteAsync(null);

        string folder = Path.Combine(_root, "Grandma's album");
        Directory.Exists(folder).ShouldBeTrue();
        _host.Opened.ShouldBe([(folder, false)]);
        vm.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task OpenWhenTheProjectIsOpenInAnotherWindowShouldStayAndSaySo()
    {
        string folder = Path.Combine(_root, "Album");
        Directory.CreateDirectory(folder);
        _host.RecentProjectFolders = [folder];
        _host.InUse = true;
        var vm = new StartViewModel(_host);
        vm.Open();

        await vm.OpenRecentCommand.ExecuteAsync(vm.Recent[0]);

        vm.IsOpen.ShouldBeTrue();
        vm.OpenError.ShouldContain("Album");

        _host.InUse = false;
        await vm.OpenRecentCommand.ExecuteAsync(vm.Recent[0]);
        vm.IsOpen.ShouldBeFalse();
        vm.OpenError.ShouldBeEmpty();
    }

    [Fact]
    public async Task RestoreOnlyShouldOpenTheNewProjectForImporting()
    {
        var vm = new StartViewModel(_host);
        vm.Open();
        vm.RestoreOnlyCommand.Execute(null);
        vm.NewName = "Loose photos";

        await vm.CreateCommand.ExecuteAsync(null);

        _host.Opened.ShouldBe([(Path.Combine(_root, "Loose photos"), true)]);
    }

    [Theory]
    [InlineData("bad:name")]
    [InlineData("a/b")]
    [InlineData("..")]
    public async Task CreateShouldRefuseNamesThatAreNotFolderNames(string name)
    {
        var vm = new StartViewModel(_host);
        vm.NewProjectCommand.Execute(null);
        vm.NewName = name;

        await vm.CreateCommand.ExecuteAsync(null);

        vm.CreateError.ShouldNotBeEmpty();
        _host.Opened.ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateShouldNotReuseAFolderThatExists()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Taken"));
        var vm = new StartViewModel(_host);
        vm.NewProjectCommand.Execute(null);
        vm.NewName = "Taken";

        await vm.CreateCommand.ExecuteAsync(null);

        vm.CreateError.ShouldContain("Taken");
        _host.Opened.ShouldBeEmpty();
    }

    [Fact]
    public void SuggestNameShouldSkipNamesInUse()
    {
        var day = new DateTime(2026, 11, 18);
        string first = StartViewModel.SuggestName(_root, day);
        Directory.CreateDirectory(Path.Combine(_root, first));

        StartViewModel.SuggestName(_root, day).ShouldBe($"{first} (2)");
    }

    [Fact]
    public void OpenShouldListRecentProjectsWithTheirContents()
    {
        string project = Path.Combine(_root, "Album");
        ProjectWorkspaceService.InitializeWorkspace(project);
        TestImageFactory.CreateStandardTwoPhotoScan(Path.Combine(ProjectWorkspaceService.GetRawScansDirectory(project), "scan_0001.png"), 300, 300);
        string missing = Path.Combine(_root, "Gone");
        _host.RecentProjectFolders = [project, missing];
        var vm = new StartViewModel(_host);

        vm.Open();

        vm.Recent.Count.ShouldBe(2);
        vm.Recent[0].Name.ShouldBe("Album");
        vm.Recent[0].Exists.ShouldBeTrue();
        vm.Recent[0].Details.ShouldBe(LocalizationService.Format(ResourceKeys.LblRecentProjectDetails, "{0} scans · {1} finished photos", 1, 0));
        vm.Recent[1].Exists.ShouldBeFalse();
        vm.CanGoBack.ShouldBeFalse(); // nothing open at launch
    }

    [Fact]
    public void BackShouldOnlyLeaveWhenAProjectIsOpen()
    {
        var vm = new StartViewModel(_host);
        vm.Open();
        vm.Back();
        vm.IsOpen.ShouldBeTrue();

        _host.OpenProjectFolder = _root;
        vm.Open();
        vm.CanGoBack.ShouldBeTrue();
        vm.Back();
        vm.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public void RemoveRecentShouldForgetTheProjectButKeepItsFolder()
    {
        string project = Path.Combine(_root, "Album");
        Directory.CreateDirectory(project);
        _host.RecentProjectFolders = [project];
        var vm = new StartViewModel(_host);
        vm.Open();

        vm.RemoveRecentCommand.Execute(vm.Recent[0]);

        vm.Recent.ShouldBeEmpty();
        _host.Forgotten.ShouldBe([project]);
        Directory.Exists(project).ShouldBeTrue();
    }

    [Fact]
    public void RecentProjectsShouldKeepTheNewestFirstWithoutDuplicates()
    {
        string a = Path.Combine(_root, "A"), b = Path.Combine(_root, "B");
        IReadOnlyList<string> list = [];

        list = RecentProjects.Add(list, a);
        list = RecentProjects.Add(list, b);
        list = RecentProjects.Add(list, a + Path.DirectorySeparatorChar);

        list.ShouldBe([a, b]);
        RecentProjects.Remove(list, b).ShouldBe([a]);

        for (int i = 0; i < 20; i++) list = RecentProjects.Add(list, Path.Combine(_root, $"P{i}"));
        list.Count.ShouldBe(RecentProjects.MaxCount);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class FakeHost : IStartHost
    {
        public IReadOnlyList<string> RecentProjectFolders { get; set; } = [];
        public string? OpenProjectFolder { get; set; }
        public string DefaultProjectsFolder { get; set; } = string.Empty;
        public List<(string Folder, bool RestoreOnly)> Opened { get; } = [];
        public List<string> Forgotten { get; } = [];

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public bool InUse { get; set; }

        public Task<bool> OpenProjectAsync(string folder, bool restoreOnly)
        {
            Opened.Add((folder, restoreOnly));
            return Task.FromResult(!InUse);
        }

        public void ForgetRecentProject(string folder) => Forgotten.Add(folder);
    }
}
