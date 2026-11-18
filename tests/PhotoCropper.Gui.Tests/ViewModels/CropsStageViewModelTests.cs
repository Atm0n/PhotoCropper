using PhotoCropper.Core;
using PhotoCropper.Core.Models;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;
using PhotoCropper.Gui.ViewModels;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Gui.Tests.ViewModels;

public sealed class CropsStageViewModelTests : IDisposable
{
    private readonly string _dir;
    private readonly ScanSessionManager _sessions = new();
    private readonly FakeCropsHost _host = new();
    private readonly UserSettings _settings = new() { AutoTuneOnScanChange = false, AutoAdjustOnLowCoverage = false };
    private int _settingsSaves;

    public CropsStageViewModelTests()
    {
        TestAppBuilder.EnsureInitialized();
        _dir = TestImageFactory.CreateTempDirectory("CropsStageViewModelTests");
    }

    public void Dispose()
    {
        _sessions.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The session manager owns and disposes its sessions")]
    private CropsStageViewModel CreateViewModel(int scanCount)
    {
        for (int i = 0; i < scanCount; i++)
        {
            string path = Path.Combine(_dir, $"scan_{i + 1:0000}.png");
            TestImageFactory.CreateStandardTwoPhotoScan(path, 800, 800);
            _sessions.Add(new ScanSessionItem(path, new DetectionOptions()));
        }
        return new CropsStageViewModel(_host, _sessions, () => _settings, () => _settingsSaves++)
        {
            OptionsDebounce = TimeSpan.FromMilliseconds(30),
        };
    }

    private static string Counter(int scan, int of) =>
        LocalizationService.Format(ResourceKeys.ScanCounter, "Scan {0} of {1}", scan, of);

    [Fact]
    public async Task LoadCurrentScan_ShowsTheScanAndItsCounterAndCoverage()
    {
        using var vm = CreateViewModel(2);

        await vm.LoadCurrentScanAsync();

        _host.Shown.ShouldHaveSingleItem().ShouldBeSameAs(_sessions.Sessions[0].Engine);
        vm.ScanCounterText.ShouldBe(Counter(1, 2));
        vm.CoverageText.ShouldNotBeEmpty();
        _sessions.Sessions[0].IsAutoTuned.ShouldBeTrue();
    }

    [Fact]
    public async Task Navigation_MovesBetweenScansAndStopsAtTheEnds()
    {
        using var vm = CreateViewModel(2);
        await vm.LoadCurrentScanAsync();

        vm.PreviousScanCommand.CanExecute(null).ShouldBeFalse();
        vm.NextScanCommand.CanExecute(null).ShouldBeTrue();

        await vm.NextScanCommand.ExecuteAsync(null);

        _sessions.CurrentIndex.ShouldBe(1);
        vm.ScanCounterText.ShouldBe(Counter(2, 2));
        _host.Shown[^1].ShouldBeSameAs(_sessions.Sessions[1].Engine);
        vm.NextScanCommand.CanExecute(null).ShouldBeFalse();
        vm.PreviousScanCommand.CanExecute(null).ShouldBeTrue();
        vm.IsNavigating.ShouldBeFalse();
    }

    [Fact]
    public async Task SliderChanges_AreSavedAtOnceButDetectOnlyOnceAfterThePause()
    {
        using var vm = CreateViewModel(1);
        await vm.LoadCurrentScanAsync();
        _host.BusyMessages.Clear();

        vm.Sensitivity = 40;
        vm.Sensitivity = 45;
        vm.EdgeThreshold = 30;
        vm.IsDetectionPending.ShouldBeTrue();

        await WaitUntil(() => _host.BusyMessages.Count > 0 && !vm.IsDetectionPending);

        _host.BusyMessages.Count.ShouldBe(1);
        var options = _sessions.Sessions[0].Options;
        options.BackgroundTolerance.ShouldBe(DetectionOptions.SensitivityToTolerance(45));
        options.CannyLowThreshold.ShouldBe(30);
        _settings.CannyLowThreshold.ShouldBe(30);
        _settingsSaves.ShouldBeGreaterThan(0);
        _sessions.Sessions[0].IsModified.ShouldBeTrue();
    }

    [Fact]
    public async Task ShowOptions_UpdatesTheControlsWithoutRedetecting()
    {
        using var vm = CreateViewModel(1);
        await vm.LoadCurrentScanAsync();
        _host.BusyMessages.Clear();

        vm.ShowOptions(new DetectionOptions { BackgroundTolerance = 10, MinAreaFactor = 0.1, CannyLowThreshold = 55 });
        await Task.Delay(100, TestContext.Current.CancellationToken);

        vm.EdgeThreshold.ShouldBe(55);
        vm.MinAreaPercent.ShouldBe(10, 0.001);
        vm.IsDetectionPending.ShouldBeFalse();
        _host.BusyMessages.ShouldBeEmpty();
        _settingsSaves.ShouldBe(0);
    }

    [Fact]
    public async Task Lookahead_PreparesTheNextScanInTheBackground()
    {
        using var vm = CreateViewModel(3);
        await vm.LoadCurrentScanAsync();

        await WaitUntil(() => _sessions.Sessions[1] is { IsActive: true, IsAutoTuned: true });

        await vm.DrainLookaheadAsync();
        _host.BusyMessages.Clear();
        await vm.NavigateAsync(forward: true);

        vm.IsPreloaded.ShouldBeTrue();
        _host.BusyMessages.ShouldBeEmpty();
    }

    [Fact]
    public void LookaheadPlan_PreparesScansAheadAndOneBehind()
    {
        LookaheadPlan.ScansToPrepare(10, 0, ahead: 4, behind: 4).ShouldBe([1, 2, 3, 4, 6]);
        LookaheadPlan.ScansToPrepare(3, 2, ahead: 4, behind: 4).ShouldBe([0, 1]);
        LookaheadPlan.ScansToPrepare(1, 0, ahead: 4, behind: 4).ShouldBeEmpty();
    }

    [Fact]
    public void LookaheadPlan_ReleasesScansFarFromTheCurrentOneAroundTheList()
    {
        LookaheadPlan.ScansToRelease(20, 0, ahead: 4).ShouldBe([6, 7, 8, 9, 10, 11, 12, 13, 14]);
        LookaheadPlan.ScansToRelease(10, 0, ahead: 4).ShouldBeEmpty();
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(20);
        }
    }

    private sealed class FakeCropsHost : ICropsStageHost
    {
        public bool IsBusy { get; private set; }
        public List<string> BusyMessages { get; } = [];
        public List<PhotoCropperEngine> Shown { get; } = [];
        public List<string> Statuses { get; } = [];

        public async Task<bool> RunBusyAsync(string message, Func<CancellationToken, Task> work)
        {
            if (IsBusy) return false;
            BusyMessages.Add(message);
            IsBusy = true;
            try
            {
                await work(CancellationToken.None);
                return true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void ShowScanResult(PhotoCropperEngine engine) => Shown.Add(engine);
        public void ShowStatus(string message) => Statuses.Add(message);
    }
}
