using Avalonia.Input;
using PhotoCropper.Core;
using PhotoCropper.Core.Models;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;
using PhotoCropper.Gui.ViewModels;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Gui.Tests.ViewModels;

public sealed class ReviewAndRefineViewModelTests : IDisposable
{
    private readonly string _dir;
    private readonly List<ScanSessionItem> _scans = [];
    private readonly UndoRedoHistory _undo = new();
    private readonly FakeHost _host;

    public ReviewAndRefineViewModelTests()
    {
        TestAppBuilder.EnsureInitialized();
        _dir = TestImageFactory.CreateTempDirectory("ReviewAndRefineViewModelTests");
        for (int i = 0; i < 2; i++)
        {
            string path = Path.Combine(_dir, $"scan_{i + 1:0000}.png");
            TestImageFactory.CreateStandardTwoPhotoScan(path);
            var session = new ScanSessionItem(path, new DetectionOptions { AutoOrientPhotos = false });
            session.Activate().DetectedPhotos.Count.ShouldBe(2);
            _scans.Add(session);
        }
        _host = new FakeHost(_scans);
    }

    public void Dispose()
    {
        foreach (var scan in _scans) scan.Dispose();
        _undo.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    // ---- review ----

    [Fact]
    public void OpenStartsOnTheSelectedPhotoOfTheCurrentScan()
    {
        _host.CurrentScanIndex = 1;
        var vm = new ReviewViewModel(_host, _undo);

        vm.Open(selectedPhotoIndex: 1).ShouldBeTrue();

        vm.IsOpen.ShouldBeTrue();
        vm.Items.Count.ShouldBe(4);
        vm.Image.ShouldNotBeNull();
        vm.CounterText.ShouldBe(LocalizationService.Format(ResourceKeys.PhotoCounter, "PHOTO {0} OF {1}", 4, 4));
        vm.ScanInfoText.ShouldStartWith("scan_0002.png");
    }

    [Fact]
    public void NextPassesTheEndSlideThenWrapsToTheFirstPhoto()
    {
        _host.CurrentScanIndex = 1;
        var vm = new ReviewViewModel(_host, _undo);
        vm.Open(selectedPhotoIndex: 1);

        vm.Next();
        vm.IsAtEnd.ShouldBeTrue();
        vm.Image.ShouldBeNull();
        vm.EndStatsText.ShouldContain("4");

        vm.Next();
        vm.IsAtEnd.ShouldBeFalse();
        vm.CounterText.ShouldBe(LocalizationService.Format(ResourceKeys.PhotoCounter, "PHOTO {0} OF {1}", 1, 4));
    }

    [Fact]
    public void RotateTurnsThePhotoAndIsUndoableOnlyForTheOpenScan()
    {
        var vm = new ReviewViewModel(_host, _undo);
        vm.Open(selectedPhotoIndex: 0);
        var photo = _scans[0].Engine!.DetectedPhotos[0];
        var before = photo.Size();

        vm.HandleKey(Key.R).ShouldBeTrue();

        _scans[0].Engine!.DetectedPhotos[0].Width.ShouldBe(before.Height);
        _undo.UndoCount.ShouldBe(1);

        vm.Previous(); // wraps to the end slide
        vm.Previous(); // last photo: scan 2, not the open scan
        vm.RotateClockwise();
        _undo.UndoCount.ShouldBe(1);
        _scans[1].IsModified.ShouldBeTrue();
    }

    [Fact]
    public void DeleteRemovesThePhotoAndRenumbersTheRestOfItsScan()
    {
        var vm = new ReviewViewModel(_host, _undo);
        vm.Open(selectedPhotoIndex: 0);

        vm.Delete();

        _scans[0].Engine!.DetectedPhotos.Count.ShouldBe(1);
        vm.Items.ShouldBe([new ReviewPhotoItem(0, 0), new ReviewPhotoItem(1, 0), new ReviewPhotoItem(1, 1)]);
        vm.IsOpen.ShouldBeTrue();
        _undo.UndoCount.ShouldBe(1);
    }

    [Fact]
    public void DeletingEveryPhotoClosesReview()
    {
        var vm = new ReviewViewModel(_host, _undo);
        vm.Open(selectedPhotoIndex: 0);

        for (int i = 0; i < 4; i++) vm.Delete();

        vm.IsOpen.ShouldBeFalse();
        _host.ClosedOn.ShouldBe((-1, -1));
    }

    [Fact]
    public void EscapeClosesOnThePhotoShown()
    {
        var vm = new ReviewViewModel(_host, _undo);
        vm.Open(selectedPhotoIndex: 0);
        vm.Next();
        vm.Next();

        vm.HandleKey(Key.Escape).ShouldBeTrue();

        vm.IsOpen.ShouldBeFalse();
        _host.ClosedOn.ShouldBe((1, 0));
        vm.HandleKey(Key.R).ShouldBeFalse("closed review takes no keys");
    }

    // ---- refine ----

    [Fact]
    public void RefineStartsFromThePhotosSuggestedRefinement()
    {
        var engine = _scans[0].Engine!;
        var vm = new RefineViewModel(_host, _undo);

        vm.Open(engine, 0, 0, () => _scans[0].IsModified = true).ShouldBeTrue();

        vm.IsOpen.ShouldBeTrue();
        vm.Preview.ShouldNotBeNull();
        var suggested = engine.GetRefinedCropRect(0);
        vm.CropRect.ShouldBe(suggested.IsEmpty || suggested.Width <= 10 || suggested.Height <= 10
            ? new System.Drawing.Rectangle(System.Drawing.Point.Empty, vm.PhotoSize)
            : suggested);
        new System.Drawing.Rectangle(System.Drawing.Point.Empty, vm.PhotoSize).Contains(vm.CropRect).ShouldBeTrue();
    }

    [Fact]
    public async Task AcceptingARefinementRecropsThePhotoInOneUndoStep()
    {
        var engine = _scans[0].Engine!;
        var vm = new RefineViewModel(_host, _undo);
        vm.Open(engine, 0, 0, () => _scans[0].IsModified = true);
        var crop = new System.Drawing.Rectangle(20, 30, vm.PhotoSize.Width / 2, vm.PhotoSize.Height / 2);

        vm.SetCrop(crop);
        await vm.AcceptAsync();

        Math.Abs(engine.DetectedPhotos[0].Width - crop.Width).ShouldBeLessThanOrEqualTo(2);
        Math.Abs(engine.DetectedPhotos[0].Height - crop.Height).ShouldBeLessThanOrEqualTo(2);
        _undo.UndoCount.ShouldBe(1);
        _host.RefinedPhoto.ShouldBe(0);
        _scans[0].IsModified.ShouldBeTrue();
        vm.IsOpen.ShouldBeFalse();
        vm.Preview.ShouldBeNull();
    }

    [Fact]
    public async Task CancellingARefinementLeavesThePhotoAlone()
    {
        var engine = _scans[0].Engine!;
        var before = engine.DetectedPhotos[0].Size();
        var vm = new RefineViewModel(_host, _undo);
        vm.Open(engine, 0, 0, () => { });
        vm.SetCrop(new System.Drawing.Rectangle(10, 10, 50, 50));

        (await vm.HandleKeyAsync(Key.Escape)).ShouldBeTrue();

        vm.IsOpen.ShouldBeFalse();
        engine.DetectedPhotos[0].Size().ShouldBe(before);
        _undo.UndoCount.ShouldBe(0);
    }

    private sealed class FakeHost(IReadOnlyList<ScanSessionItem> scans) : IReviewHost, IRefineHost
    {
        public IReadOnlyList<ScanSessionItem> Scans { get; } = scans;
        public int CurrentScanIndex { get; set; }
        public (int Scan, int Photo)? ClosedOn { get; private set; }
        public int? RefinedPhoto { get; private set; }

        public void DisposeLater(IReadOnlyCollection<IDisposable> items)
        {
            foreach (var item in items) item.Dispose();
        }

        public void ReviewClosed(int scanIndex, int photoIndex) => ClosedOn = (scanIndex, photoIndex);
        public void ShowError(string title, string message) { }
        public void ShowStatus(string message) { }
        public void Refined(PhotoCropperEngine engine, int photoIndex) => RefinedPhoto = photoIndex;

        public async Task<bool> RunBusyAsync(string message, Func<CancellationToken, Task> work)
        {
            await work(CancellationToken.None);
            return true;
        }
    }
}
