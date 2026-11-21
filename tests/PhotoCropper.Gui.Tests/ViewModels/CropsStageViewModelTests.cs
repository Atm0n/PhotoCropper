using PhotoCropper.Core;
using PhotoCropper.Core.Models;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Controls;
using PhotoCropper.Gui.Services;
using PhotoCropper.Gui.ViewModels;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Gui.Tests.ViewModels;

public sealed class CropsStageViewModelTests : IDisposable
{
    private readonly string _dir;
    private readonly ScanSessionManager _sessions = new();
    private readonly FakeCropsHost _host = new();
    private readonly UndoRedoHistory _undo = new();
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
        _undo.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The session manager owns and disposes its sessions")]
    private CropsStageViewModel CreateViewModel(int scanCount, int scanSize = 800)
    {
        for (int i = 0; i < scanCount; i++)
        {
            string path = Path.Combine(_dir, $"scan_{i + 1:0000}.png");
            TestImageFactory.CreateStandardTwoPhotoScan(path, scanSize, scanSize);
            _sessions.Add(new ScanSessionItem(path, new DetectionOptions()));
        }
        return new CropsStageViewModel(_host, _sessions, _undo, () => _settings, () => _settingsSaves++)
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
    public async Task LoadCurrentScan_WhileTheScanStagePreparesTheSameScan_ListsEachPhotoOnce()
    {
        // Opening a project: the Scan stage's preview and Crops both prepare the first scan, auto-tuning it.
        _settings.AutoTuneOnScanChange = true;
        using var vm = CreateViewModel(1);
        var session = _sessions.Sessions[0];

        await Task.WhenAll(vm.PrepareScanAsync(session, CancellationToken.None), vm.LoadCurrentScanAsync());

        var engine = session.Engine.ShouldNotBeNull();
        engine.DetectedPhotos.Count.ShouldBe(engine.AcceptedCandidates.Count);
        vm.Photos.Count.ShouldBe(engine.AcceptedCandidates.Count);
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

    // ---- box editing ----

    private async Task<(CropsStageViewModel Vm, PhotoCropperEngine Engine)> OpenScanWithTwoPhotosAsync()
    {
        var vm = CreateViewModel(1, scanSize: 2000);
        await vm.LoadCurrentScanAsync();
        var engine = _sessions.Sessions[0].Engine!;
        engine.DetectedPhotos.Count.ShouldBe(2);
        return (vm, engine);
    }

    private static CropBoxGeometry Geometry(PhotoCropperEngine engine, int index) =>
        CropBoxGeometry.For(engine.AcceptedCandidates[index], new System.Drawing.Size(engine.Original.Width, engine.Original.Height), engine.OverlayScale);

    [Fact]
    public async Task ClickOnDeleteBadge_DeletesThatPhotoAndCanBeUndone()
    {
        var (vm, engine) = await OpenScanWithTwoPhotosAsync();
        using var _ = vm;

        vm.ClickAt(Geometry(engine, 0).DeleteBadge);

        engine.DetectedPhotos.Count.ShouldBe(1);
        engine.AcceptedCandidates.Count.ShouldBe(1);
        _undo.CanUndo.ShouldBeTrue();
        _sessions.Sessions[0].IsModified.ShouldBeTrue();
    }

    [Fact]
    public async Task ClickAt_SaysWhetherItWasOnABox()
    {
        // A click that isn't on a box finds the photo there (the view does that when ClickAt says so).
        var (vm, engine) = await OpenScanWithTwoPhotosAsync();
        using var _ = vm;

        vm.ClickAt(new System.Drawing.PointF(2, 2)).ShouldBeFalse();
        engine.AcceptedCandidates.Count.ShouldBe(2);

        vm.ClickAt(engine.AcceptedCandidates[1].Rotated.Center).ShouldBeTrue();
        vm.SelectedPhotoIndex.ShouldBe(1);
    }

    [Fact]
    public async Task DeleteBoxAt_OnlyOnADeleteBadge()
    {
        var (vm, engine) = await OpenScanWithTwoPhotosAsync();
        using var _ = vm;

        vm.DeleteBoxAt(engine.AcceptedCandidates[1].Rotated.Center).ShouldBeFalse();
        engine.AcceptedCandidates.Count.ShouldBe(2);

        vm.DeleteBoxAt(Geometry(engine, 0).DeleteBadge).ShouldBeTrue();
        engine.AcceptedCandidates.Count.ShouldBe(1);
        _undo.CanUndo.ShouldBeTrue();
    }

    [Fact]
    public async Task ClickInsideBox_SelectsItsPhoto()
    {
        var (vm, engine) = await OpenScanWithTwoPhotosAsync();
        using var _ = vm;

        vm.ClickAt(engine.AcceptedCandidates[1].Rotated.Center);

        vm.SelectedPhotoIndex.ShouldBe(1);
        engine.DetectedPhotos.Count.ShouldBe(2);
    }

    [Fact]
    public async Task DraggingTheRotationHandle_TurnsTheBoxAndRecordsUndo()
    {
        var (vm, engine) = await OpenScanWithTwoPhotosAsync();
        using var _ = vm;
        var box = engine.AcceptedCandidates[0];
        var center = box.Rotated.Center;
        var handle = Geometry(engine, 0).RotationHandle;

        vm.PressAt(handle).ShouldBe(BoxGesture.Rotate);
        // Swing the handle a quarter turn around the box's centre.
        var turned = new System.Drawing.PointF(center.X + (center.Y - handle.Y), center.Y + (handle.X - center.X));
        vm.DragTo(turned);
        _host.BoxRepaints.ShouldBeGreaterThan(0);
        vm.Release(wasTap: false);

        Math.Abs(engine.AcceptedCandidates[0].Rotated.Angle - box.Rotated.Angle).ShouldBeGreaterThan(45);
        _undo.UndoCount.ShouldBe(1);
    }

    [Fact]
    public async Task PressOnACornerOfABoxNotSelected_DrawsInstead()
    {
        // Its handles aren't shown, so its corners don't grab: select the box to resize it.
        var (vm, engine) = await OpenScanWithTwoPhotosAsync();
        using var _ = vm;
        vm.SelectPhoto(0);

        vm.PressAt(Geometry(engine, 1).Vertices[2]).ShouldBe(BoxGesture.Draw);
        vm.SelectedBoxIndex.ShouldBe(0);
    }

    [Fact]
    public async Task DraggingACorner_ResizesTheBoxAndReExtractsThePhoto()
    {
        var (vm, engine) = await OpenScanWithTwoPhotosAsync();
        using var _ = vm;
        int index = engine.AcceptedCandidates.ToList().FindIndex(c => Math.Abs(c.Rotated.Angle % 90) < 1);
        index.ShouldBeGreaterThanOrEqualTo(0);
        var geometry = Geometry(engine, index);
        int corner = Array.FindIndex(geometry.Vertices, v => !v.Equals(geometry.DeleteBadge));
        var start = geometry.Vertices[corner];
        var opposite = geometry.Vertices[(corner + 2) % 4];
        int before = engine.DetectedPhotos[index].Width * engine.DetectedPhotos[index].Height;

        // Only the selected box has handles.
        vm.SelectPhoto(index);
        vm.PressAt(start).ShouldBe(BoxGesture.Resize);
        // Pull the corner 20 % towards the opposite one: the box shrinks.
        vm.DragTo(new System.Drawing.PointF(start.X + (opposite.X - start.X) * 0.2f, start.Y + (opposite.Y - start.Y) * 0.2f));
        vm.Release(wasTap: false);

        int after = engine.DetectedPhotos[index].Width * engine.DetectedPhotos[index].Height;
        after.ShouldBeLessThan(before);
        _undo.UndoCount.ShouldBe(1);
    }

    [Fact]
    public async Task TapOnTheTopRightCorner_DeletesTheBox()
    {
        var (vm, engine) = await OpenScanWithTwoPhotosAsync();
        using var _ = vm;

        vm.PressAt(Geometry(engine, 0).DeleteBadge).ShouldBe(BoxGesture.Resize);
        vm.Release(wasTap: true);

        engine.DetectedPhotos.Count.ShouldBe(1);
    }

    [Fact]
    public async Task DrawingARectangle_AddsAManualCropAndSelectsIt()
    {
        var (vm, engine) = await OpenScanWithTwoPhotosAsync();
        using var _ = vm;

        vm.PressAt(new System.Drawing.PointF(1600, 200)).ShouldBe(BoxGesture.Draw);
        await vm.AddManualCropAsync(new System.Drawing.Rectangle(1500, 150, 300, 200));

        engine.DetectedPhotos.Count.ShouldBe(3);
        vm.SelectedPhotoIndex.ShouldBe(2);
        _undo.UndoCount.ShouldBe(1);
    }

    [Fact]
    public async Task PickingABackgroundColor_AutoTunesDetectionForIt()
    {
        var (vm, engine) = await OpenScanWithTwoPhotosAsync();
        using var _ = vm;
        _host.BusyMessages.Clear();
        _host.Shown.Clear();

        await vm.UseBackgroundColorAtAsync(new System.Drawing.Point(20, 20));

        engine.CustomBackgroundColorHsv.ShouldNotBeNull();
        engine.CurrentOptions.CustomBackgroundColorHsv.ShouldNotBeNull("auto-tune must keep the picked colour");
        engine.DetectedPhotos.Count.ShouldBe(2);
        _host.BusyMessages.Count.ShouldBe(1);
        _host.Shown.ShouldHaveSingleItem().ShouldBeSameAs(engine);
        _host.Statuses[^1].ShouldContain("2");
        _sessions.Sessions[0].IsModified.ShouldBeTrue();
    }

    [Fact]
    public async Task ResettingTheBackgroundColor_DetectsWithTheAutomaticBackground()
    {
        var (vm, engine) = await OpenScanWithTwoPhotosAsync();
        using var _ = vm;
        await vm.UseBackgroundColorAtAsync(new System.Drawing.Point(20, 20));
        _host.Shown.Clear();

        await vm.ResetBackgroundColorAsync();

        engine.CustomBackgroundColorHsv.ShouldBeNull();
        _host.Shown.ShouldHaveSingleItem();
        engine.DetectedPhotos.Count.ShouldBe(2);
    }

    // ---- photo list ----

    [Fact]
    public async Task OpeningAScan_ListsItsPhotosAndShowsTheFirst()
    {
        var (vm, engine) = await OpenScanWithTwoPhotosAsync();
        using var _ = vm;

        vm.Photos.Count.ShouldBe(2);
        vm.HasPhotos.ShouldBeTrue();
        vm.SelectedPhotoIndex.ShouldBe(0);
        vm.Photos[0].IsSelected.ShouldBeTrue();
        vm.PhotoCounterText.ShouldBe(LocalizationService.Format(ResourceKeys.PhotoCounter, "PHOTO {0} OF {1}", 1, 2));
        vm.CanRefine.ShouldBeTrue();
        vm.PreviousPhotoCommand.CanExecute(null).ShouldBeFalse();
        vm.NextPhotoCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task DeletingThePhotoInTheCarousel_RemovesItAndItsBox()
    {
        var (vm, engine) = await OpenScanWithTwoPhotosAsync();
        using var _ = vm;
        vm.NextPhotoCommand.Execute(null);

        vm.DeleteSelectedPhotos();

        engine.DetectedPhotos.Count.ShouldBe(1);
        engine.AcceptedCandidates.Count.ShouldBe(1);
        vm.Photos.Count.ShouldBe(1);
        vm.SelectedPhotoIndex.ShouldBe(0);
        _undo.UndoCount.ShouldBe(1);
        _host.Shown[^1].ShouldBeSameAs(engine);
    }

    [Fact]
    public async Task GridSelection_DrivesTheLabelsAndBatchDeleteIsOneUndoStep()
    {
        var (vm, engine) = await OpenScanWithTwoPhotosAsync();
        using var _ = vm;

        vm.SelectAllCommand.Execute(null);

        vm.SelectedPhotoIndices().Count.ShouldBe(2);
        vm.IsSelectionTextVisible.ShouldBeTrue();
        vm.DeleteButtonText.ShouldBe(LocalizationService.Format(ResourceKeys.BtnDeleteCount, "Delete {0} photos", 2));
        vm.CanRefine.ShouldBeFalse();

        vm.DeleteSelectedPhotos();

        engine.DetectedPhotos.Count.ShouldBe(0);
        vm.HasPhotos.ShouldBeFalse();
        vm.IsGridVisible.ShouldBeFalse();
        _undo.UndoCount.ShouldBe(1);
    }

    [Fact]
    public async Task SelectingOneGridPhoto_MakesItTheSelectedPhoto()
    {
        var (vm, _) = await OpenScanWithTwoPhotosAsync();
        using var __ = vm;

        vm.Photos[0].IsSelected = false;
        vm.Photos[1].IsSelected = true;

        vm.SelectedPhotoIndex.ShouldBe(1);
        vm.SelectedPhotoIndices().ShouldBe([1]);
    }

    [Fact]
    public async Task RotatingTheSelectedPhoto_TurnsItAndKeepsItShown()
    {
        var (vm, engine) = await OpenScanWithTwoPhotosAsync();
        using var _ = vm;
        vm.SelectPhoto(1);
        var before = engine.DetectedPhotos[1].Size();

        await vm.RotateSelectedPhotosCommand.ExecuteAsync(null);

        engine.DetectedPhotos[1].Width.ShouldBe(before.Height);
        engine.DetectedPhotos[1].Height.ShouldBe(before.Width);
        vm.SelectedPhotoIndex.ShouldBe(1);
        _undo.UndoCount.ShouldBe(1);
    }

    [Fact]
    public async Task HoldToCompare_SwapsTheShownPhotoForTheOriginalAndBack()
    {
        var (vm, _) = await OpenScanWithTwoPhotosAsync();
        using var __ = vm;
        var shown = vm.Photos[0].Image;

        vm.ShowOriginal(true);
        vm.Photos[0].Image.ShouldNotBeSameAs(shown);
        var original = vm.Photos[0].Image;

        vm.ShowOriginal(false);
        vm.Photos[0].Image.ShouldNotBeSameAs(original);
    }

    [Fact]
    public void ResizeFromCorner_KeepsTheOppositeCorner()
    {
        var box = new CropCandidate
        {
            Rotated = new RotatedBox(new System.Drawing.PointF(100, 100), new System.Drawing.SizeF(100, 50), 0),
            ShapePoints = [],
        };
        var vertices = box.Rotated.GetVertices();
        int corner = 0;
        var opposite = vertices[2];

        var resized = BoxEditMath.ResizeFromCorner(box, corner, new System.Drawing.PointF(opposite.X - 200, opposite.Y - 80));

        resized.Rotated.Size.Width.ShouldBe(200, 0.01);
        resized.Rotated.Size.Height.ShouldBe(80, 0.01);
        resized.Rotated.GetVertices().ShouldContain(v => Math.Abs(v.X - opposite.X) < 0.01 && Math.Abs(v.Y - opposite.Y) < 0.01);
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
        public int BoxRepaints { get; private set; }
        public void ShowBoxes(PhotoCropperEngine engine) => BoxRepaints++;
        public void DisposeLater(IReadOnlyCollection<IDisposable> items)
        {
            foreach (var item in items) item.Dispose();
        }
        public void ShowStatus(string message) => Statuses.Add(message);
    }
}
