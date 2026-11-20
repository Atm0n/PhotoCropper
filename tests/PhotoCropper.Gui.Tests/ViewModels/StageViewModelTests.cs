using OpenCvSharp;
using PhotoCropper.Core.Models;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;
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
        using (var scan = new Mat(200, 200, MatType.CV_8UC3)) { scan.SetTo(new Scalar(230, 230, 230)); Cv2.ImWrite(scanPath, scan); }
        var entry = new WorkspaceScanEntry { RelativePath = Path.GetRelativePath(_workDir, scanPath) };
        _host.Workspace!.Scans.Add(entry);

        using var photo = new Mat(40, 60, MatType.CV_8UC3);
        photo.SetTo(new Scalar(90, 120, 150));
        var crop = new WorkspaceCropData { CenterX = 50, CenterY = 50, Width = 60, Height = 40 };
        var saved = PhotoPipelineService.SaveCroppedPhotos(_workDir, _host.Workspace, entry, [new CroppedPhotoInput(photo, crop)],
            new RestorationSettings { RestoreColors = restoreColors })[0];
        // As the app and the CLI do: the scan's boxes are recorded with its photos. Without them the Scan stage's
        // preview asks the host to detect, the fake host finds nothing and flags the scan empty in the background.
        entry.SetFinalCrops([crop]);
        return saved;
    }

    [Fact]
    public void FinishStage_Refresh_ShouldListCroppedPhotos()
    {
        AddCroppedPhoto("scan_0001.png");
        AddCroppedPhoto("scan_0002.png");
        using var vm = Finish();

        vm.Refresh();

        vm.HasProject.ShouldBeTrue();
        vm.Photos.Select(p => p.Title).ShouldBe(["scan_0001_p01", "scan_0002_p01"]);
        vm.ExportCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void FinishStage_WithoutProject_ShouldShowNoProject()
    {
        using var vm = Finish(new FakeHost(null, null));

        vm.Refresh();

        vm.HasProject.ShouldBeFalse();
        vm.Photos.ShouldBeEmpty();
        vm.ExportCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task FinishStage_Export_ShouldWriteFinalsAndClearStaleness()
    {
        AddCroppedPhoto("scan_0001.png");
        using var vm = Finish();
        vm.Refresh();

        await vm.ExportCommand.ExecuteAsync(null);

        vm.Photos[0].IsStale.ShouldBeFalse();
        File.Exists(vm.Photos[0].Photo.Final!.GetFullPath(_workDir)).ShouldBeTrue();
        vm.ExportCommand.CanExecute(null).ShouldBeFalse();
        _host.Saves.ShouldBeGreaterThan(0);
        _host.Notified.ShouldHaveSingleItem().Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task FinishStage_ChangingRestoration_ShouldMarkStaleAndSaveImmediately()
    {
        AddCroppedPhoto("scan_0001.png");
        using var vm = Finish();
        vm.Refresh();
        await vm.ExportCommand.ExecuteAsync(null);
        int savesBefore = _host.Saves;

        vm.Photos[0].RestoreColors = true;

        vm.Photos[0].Photo.Restoration.RestoreColors.ShouldBeTrue();
        vm.Photos[0].IsStale.ShouldBeTrue();
        _host.Saves.ShouldBe(savesBefore + 1);
        vm.ExportCommand.CanExecute(null).ShouldBeTrue();
    }

    /// <summary>A Finish view-model that commits changes and renders previews immediately (no timers in tests).</summary>
    private FinishStageViewModel Finish(FakeHost? host = null) =>
        new(host ?? _host) { ChangeDebounce = TimeSpan.Zero, FullQualityDelay = TimeSpan.Zero };

    [Fact]
    public void FinishStage_CopySettingsToAll_ShouldGiveEveryPhotoTheSelectedPhotosSettings()
    {
        AddCroppedPhoto("scan_0001.png");
        AddCroppedPhoto("scan_0002.png");
        using var vm = Finish();
        vm.Refresh();
        vm.CopySettingsToAllCommand.CanExecute(null).ShouldBeFalse("nothing is selected yet");

        vm.SelectedPhoto = vm.Photos[0];
        vm.Photos[0].ApplyPresetCommand.Execute("sepia");
        vm.Photos[0].Warmth = 30;
        vm.CopySettingsToAllCommand.Execute(null);

        vm.Photos[1].Photo.Restoration.ShouldBe(RestorationSettings.Sepia with { Warmth = 30 });
    }

    [Fact]
    public void FinishStage_CtrlAndShiftClick_ShouldMarkPhotosForTheBulkActions()
    {
        for (int i = 1; i <= 4; i++) AddCroppedPhoto($"scan_000{i}.png");
        using var vm = Finish();
        vm.Refresh();

        vm.ToggleMark(vm.Photos[0], range: false);          // Ctrl+click
        vm.ToggleMark(vm.Photos[2], range: true);           // Shift+click: 0..2
        vm.MarkedCount.ShouldBe(3);
        vm.Photos[3].IsMarked.ShouldBeFalse();

        vm.ToggleMark(vm.Photos[1], range: false);          // Ctrl+click again: unmarks
        vm.MarkedCount.ShouldBe(2);
        vm.HasMarks.ShouldBeTrue();

        vm.MarkAllCommand.Execute(null);
        vm.MarkedCount.ShouldBe(4);
        vm.ClearMarksCommand.Execute(null);
        vm.HasMarks.ShouldBeFalse();
        vm.SelectedPhoto.ShouldBeNull("marking never opens the editor");
    }

    [Fact]
    public void FinishStage_BulkActions_ShouldChangeOnlyTheMarkedPhotos()
    {
        for (int i = 1; i <= 3; i++) AddCroppedPhoto($"scan_000{i}.png");
        using var vm = Finish();
        vm.Refresh();
        vm.Photos[0].IsMarked = true;
        vm.Photos[2].IsMarked = true;

        vm.ApplyPresetToMarkedCommand.Execute("bw");
        vm.MarkedTrimWhiteBorders = true;

        foreach (int i in new[] { 0, 2 })
        {
            vm.Photos[i].Photo.Restoration.ShouldBe(RestorationSettings.BlackAndWhite with { TrimWhiteBorders = true });
        }
        vm.Photos[1].Photo.Restoration.ShouldBe(RestorationSettings.None);

        // A preset afterwards changes the colours but keeps the trim choice.
        vm.ApplyPresetToMarkedCommand.Execute("sepia");
        vm.Photos[0].Photo.Restoration.ShouldBe(RestorationSettings.Sepia with { TrimWhiteBorders = true });

        vm.MarkedTrimWhiteBorders = false;
        vm.Photos[2].TrimWhiteBorders.ShouldBeFalse();
    }

    [Fact]
    public void FinishStage_BulkCheckboxes_ShouldShowAllNoneOrSomeAndTurnOnForAll()
    {
        for (int i = 1; i <= 3; i++) AddCroppedPhoto($"scan_000{i}.png");
        using var vm = Finish();
        vm.Refresh();
        vm.MarkAllCommand.Execute(null);
        vm.MarkedRestoreColors.ShouldBe(false);

        vm.Photos[1].RestoreColors = true;              // changed on one card
        vm.MarkedRestoreColors.ShouldBeNull();          // some: a dash

        vm.MarkedRestoreColors = false;                 // a click on the dash (the checkbox may send false) ...
        vm.Photos.ShouldAllBe(p => p.RestoreColors);    // ... turns it on for all
        vm.MarkedRestoreColors.ShouldBe(true);

        vm.MarkedRemoveDust = true;
        vm.Photos.ShouldAllBe(p => p.RemoveDust);
        vm.MarkedRemoveDust = false;                    // all had it: off for all
        vm.Photos.ShouldAllBe(p => !p.RemoveDust);

        vm.Photos[0].IsMarked = false;                  // the state follows the selection
        vm.Photos[0].RestoreColors = false;
        vm.MarkedRestoreColors.ShouldBe(true);
    }

    [Fact]
    public async Task FinishStage_ExportSelected_ShouldWriteOnlyTheMarkedPhotos()
    {
        AddCroppedPhoto("scan_0001.png");
        AddCroppedPhoto("scan_0002.png");
        using var vm = Finish();
        vm.Refresh();
        vm.Photos[1].IsMarked = true;

        await vm.ExportMarkedCommand.ExecuteAsync(null);

        vm.Photos[1].IsStale.ShouldBeFalse();
        vm.Photos[0].IsStale.ShouldBeTrue();
        vm.Photos[0].Photo.Final.ShouldBeNull();
    }

    [Fact]
    public async Task FinishStage_RepairStroke_ShouldBeStoredWithTheBrushSizeAndPhotoSize()
    {
        AddCroppedPhoto("scan_0001.png");
        using var vm = Finish();
        vm.Refresh();
        vm.SelectedPhoto = vm.Photos[0];
        await vm.UpdatePreviewAsync(); // learns the photo's full size
        vm.BrushSize = 20;
        int savesBefore = _host.Saves;

        vm.AddRepairStroke([(0.1, 0.5), (0.9, 0.5)]);

        var photo = vm.Photos[0].Photo;
        photo.Repairs.Count.ShouldBe(1);
        photo.Repairs[0].Radius.ShouldBe(0.02f);
        photo.Repairs[0].Points.ShouldBe([0.1f, 0.5f, 0.9f, 0.5f]);
        (photo.RepairImageWidth, photo.RepairImageHeight).ShouldBe((60, 40));
        photo.IsFinalStale.ShouldBeTrue();
        _host.Saves.ShouldBe(savesBefore + 1);
        vm.Photos[0].RepairCount.ShouldBe(1);
    }

    private FinishStageViewModel FinishWithAi(bool installed, Func<IProgress<double>, CancellationToken, Task>? download = null)
    {
        var vm = Finish();
        vm.IsAiModelInstalled = installed;
        vm.CheckAiModelInstalled = () => true;
        vm.PreloadModel = () => { _preloads++; return Task.CompletedTask; };
        vm.DownloadModel = download ?? ((_, _) => Task.CompletedTask);
        return vm;
    }

    private int _preloads;

    [Theory]
    [InlineData(true, true, RepairMethod.Ai)]
    [InlineData(false, true, RepairMethod.Quick)]
    [InlineData(true, false, RepairMethod.Quick)] // AI chosen but not downloaded yet
    public async Task FinishStage_NewStrokes_ShouldUseTheChosenFillMethod(bool useAi, bool installed, RepairMethod expected)
    {
        AddCroppedPhoto("scan_0001.png");
        using var vm = FinishWithAi(installed);
        vm.Refresh();
        vm.SelectedPhoto = vm.Photos[0];
        await vm.UpdatePreviewAsync();
        vm.UseAiFill = useAi;

        vm.AddRepairStroke([(0.5, 0.5)]);

        vm.Photos[0].Photo.Repairs.Single().Method.ShouldBe(expected);
        vm.ShowAiDownload.ShouldBe(useAi && !installed);
    }

    [Fact]
    public void FinishStage_ChoosingAiFill_ShouldLoadTheModelInTheBackground()
    {
        using var vm = FinishWithAi(installed: true);

        vm.UseAiFill = true;

        _preloads.ShouldBe(1);
    }

    [Fact]
    public async Task FinishStage_DownloadAiModel_ShouldInstallItAndHideTheOffer()
    {
        using var vm = FinishWithAi(installed: false);
        vm.UseAiFill = true;
        vm.ShowAiDownload.ShouldBeTrue();

        await vm.DownloadAiModelCommand.ExecuteAsync(null);

        vm.IsAiModelInstalled.ShouldBeTrue();
        vm.ShowAiDownload.ShouldBeFalse();
        vm.IsAiModelDownloading.ShouldBeFalse();
        vm.AiDownloadError.ShouldBeNull();
        _preloads.ShouldBe(1, "loaded right away, since AI fill is chosen");
    }

    [Fact]
    public async Task FinishStage_FailedAiDownload_ShouldShowWhy()
    {
        using var vm = FinishWithAi(installed: false, download: (_, _) => throw new InvalidDataException("corrupted"));
        vm.CheckAiModelInstalled = () => false;
        vm.UseAiFill = true;

        await vm.DownloadAiModelCommand.ExecuteAsync(null);

        vm.AiDownloadError.ShouldBe("corrupted");
        vm.ShowAiDownload.ShouldBeTrue("the offer stays so it can be retried");
        vm.IsAiModelDownloading.ShouldBeFalse();
    }

    [Fact]
    public async Task FinishStage_CancelledAiDownload_ShouldNotBeAnError()
    {
        using var vm = FinishWithAi(installed: false, download: (_, token) => Task.Delay(Timeout.Infinite, token));
        vm.CheckAiModelInstalled = () => false;
        vm.UseAiFill = true;

        Task download = vm.DownloadAiModelCommand.ExecuteAsync(null);
        vm.IsAiModelDownloading.ShouldBeTrue();
        vm.DownloadAiModelCommand.CanExecute(null).ShouldBeFalse("one download at a time");
        vm.CancelAiDownloadCommand.Execute(null);
        await download;

        vm.AiDownloadError.ShouldBeNull();
        vm.IsAiModelDownloading.ShouldBeFalse();
        vm.IsAiModelInstalled.ShouldBeFalse();
    }

    [Fact]
    public void FinishStage_NextAndPreviousPhoto_ShouldStepThroughThePhotosAndWrapAround()
    {
        AddCroppedPhoto("scan_0001.png");
        AddCroppedPhoto("scan_0002.png");
        AddCroppedPhoto("scan_0003.png");
        using var vm = Finish();
        vm.Refresh();
        vm.SelectedPhoto = vm.Photos[0];

        vm.NextPhotoCommand.Execute(null);
        vm.SelectedPhoto.ShouldBe(vm.Photos[1]);
        vm.PreviousPhotoCommand.Execute(null);
        vm.PreviousPhotoCommand.Execute(null);
        vm.SelectedPhoto.ShouldBe(vm.Photos[2], "wraps to the last photo");
        vm.NextPhotoCommand.Execute(null);
        vm.SelectedPhoto.ShouldBe(vm.Photos[0], "wraps to the first photo");
    }

    [Fact]
    public void FinishStage_NextPhoto_WithTheEditorClosed_ShouldDoNothing()
    {
        AddCroppedPhoto("scan_0001.png");
        using var vm = Finish();
        vm.Refresh();

        vm.NextPhotoCommand.Execute(null);

        vm.SelectedPhoto.ShouldBeNull();
    }

    [Fact]
    public async Task FinishPhoto_UndoAndClearRepairs()
    {
        AddCroppedPhoto("scan_0001.png");
        using var vm = Finish();
        vm.Refresh();
        vm.SelectedPhoto = vm.Photos[0];
        await vm.UpdatePreviewAsync();
        vm.AddRepairStroke([(0.2, 0.2)]);
        vm.AddRepairStroke([(0.8, 0.8)]);

        vm.Photos[0].UndoRepairCommand.Execute(null);
        vm.Photos[0].Photo.Repairs.Single().Points.ShouldBe([0.2f, 0.2f]);

        vm.Photos[0].ClearRepairsCommand.Execute(null);
        vm.Photos[0].HasRepairs.ShouldBeFalse();
    }

    [Fact]
    public async Task FinishStage_RepairsFromAnEarlierCropSize_ShouldBeFlagged()
    {
        var photo = AddCroppedPhoto("scan_0001.png");
        photo.RepairImageWidth = 999;
        photo.RepairImageHeight = 999;
        photo.Repairs.Add(new RepairStroke { Radius = 0.01f, Points = { 0.5f, 0.5f } });
        using var vm = Finish();
        vm.Refresh();

        vm.SelectedPhoto = vm.Photos[0];
        await vm.UpdatePreviewAsync();

        vm.RepairsOutdated.ShouldBeTrue();
    }

    [Fact]
    public async Task FinishStage_CopySettingsToAll_ShouldNotCopyRepairs()
    {
        AddCroppedPhoto("scan_0001.png");
        AddCroppedPhoto("scan_0002.png");
        using var vm = Finish();
        vm.Refresh();
        vm.SelectedPhoto = vm.Photos[0];
        await vm.UpdatePreviewAsync();
        vm.AddRepairStroke([(0.5, 0.5)]);

        vm.CopySettingsToAllCommand.Execute(null);

        vm.Photos[1].HasRepairs.ShouldBeFalse("repairs belong to one photo");
    }

    [Fact]
    public void FinishPhoto_Presets_ShouldReplaceAllSettings()
    {
        AddCroppedPhoto("scan_0001.png");
        using var vm = Finish();
        vm.Refresh();
        var photo = vm.Photos[0];
        photo.Brightness = 40;

        photo.ApplyPresetCommand.Execute("dust");

        photo.Photo.Restoration.ShouldBe(RestorationSettings.OnlyCleanDust);
        photo.RestoreColors.ShouldBeFalse();
        photo.RemoveDust.ShouldBeTrue();
        photo.Brightness.ShouldBe(0);
    }

    [Fact]
    public void FinishPhoto_SettingsShouldUpdateTheRecord()
    {
        AddCroppedPhoto("scan_0001.png");
        using var vm = Finish();
        vm.Refresh();
        var photo = vm.Photos[0];

        photo.StrengthIndex = 2;
        photo.ColorModeIndex = 3;
        photo.AutoSaturation = false;
        photo.Contrast = 25.4;
        photo.Tint = -150;

        var s = photo.Photo.Restoration;
        s.Strength.ShouldBe(RestorationStrength.Strong);
        s.ColorMode.ShouldBe(PhotoColorMode.BlackAndWhiteNeutral);
        s.AutoSaturation.ShouldBeFalse();
        s.Contrast.ShouldBe(25);
        s.Tint.ShouldBe(-100, "adjustments are clamped to -100..100");

        photo.ResetAdjustmentsCommand.Execute(null);
        photo.Photo.Restoration.HasManualAdjustments.ShouldBeFalse();
        photo.Photo.Restoration.Strength.ShouldBe(RestorationStrength.Strong, "reset only clears the manual sliders");
    }

    [Fact]
    public async Task FinishStage_RapidChanges_ShouldBeSavedOnceAfterTheDelay()
    {
        AddCroppedPhoto("scan_0001.png");
        using var vm = new FinishStageViewModel(_host) { ChangeDebounce = TimeSpan.FromMilliseconds(80), FullQualityDelay = TimeSpan.Zero };
        vm.Refresh();
        int savesBefore = _host.Saves;

        for (int i = 1; i <= 10; i++) vm.Photos[0].Brightness = i; // like dragging a slider

        _host.Saves.ShouldBe(savesBefore, "nothing is written while the slider moves");
        await Task.Delay(400, TestContext.Current.CancellationToken);
        _host.Saves.ShouldBe(savesBefore + 1);
        vm.Photos[0].Photo.Restoration.Brightness.ShouldBe(10);
    }

    [Fact]
    public async Task FinishStage_SelectingAPhoto_ShouldShowTheSameImageBeforeAndAfterWithoutRestoration()
    {
        AddCroppedPhoto("scan_0001.png");
        using var vm = Finish();
        vm.Refresh();

        vm.SelectedPhoto = vm.Photos[0];
        await vm.UpdatePreviewAsync();

        vm.HasSelection.ShouldBeTrue();
        vm.BeforePreview.ShouldNotBeNull();
        vm.AfterPreview.ShouldBeSameAs(vm.BeforePreview);
        vm.IsPreviewBusy.ShouldBeFalse();
    }

    [Fact]
    public async Task FinishStage_TurningRestorationOn_ShouldRenderASeparateAfterImage()
    {
        AddCroppedPhoto("scan_0001.png");
        using var vm = Finish();
        vm.Refresh();
        vm.SelectedPhoto = vm.Photos[0];
        await vm.UpdatePreviewAsync();

        vm.Photos[0].RestoreColors = true;
        await vm.UpdatePreviewAsync();

        vm.AfterPreview.ShouldNotBeNull();
        vm.AfterPreview.ShouldNotBeSameAs(vm.BeforePreview);
        vm.AfterPreview!.PixelSize.ShouldBe(vm.BeforePreview!.PixelSize);
    }

    [Fact]
    public async Task FinishStage_ClosePreview_ShouldClearTheSelectionAndImages()
    {
        AddCroppedPhoto("scan_0001.png");
        using var vm = Finish();
        vm.Refresh();
        vm.SelectedPhoto = vm.Photos[0];
        await vm.UpdatePreviewAsync();

        vm.ClosePreviewCommand.Execute(null);
        await vm.UpdatePreviewAsync();

        vm.HasSelection.ShouldBeFalse();
        vm.BeforePreview.ShouldBeNull();
        vm.AfterPreview.ShouldBeNull();
    }

    [Fact]
    public async Task FinishStage_Refresh_ShouldKeepThePreviewedPhotoSelected()
    {
        AddCroppedPhoto("scan_0001.png");
        AddCroppedPhoto("scan_0002.png");
        using var vm = Finish();
        vm.Refresh();
        vm.SelectedPhoto = vm.Photos[1];
        await vm.UpdatePreviewAsync();

        vm.Refresh();

        vm.SelectedPhoto.ShouldNotBeNull();
        vm.SelectedPhoto!.Title.ShouldBe("scan_0002_p01");
    }

    [Fact]
    public void ScanStage_Refresh_ShouldShowCountsFromSavedCropsAndFlagEmptyScans()
    {
        AddCroppedPhoto("scan_0001.png");
        string emptyScan = Path.Combine(ProjectWorkspaceService.GetRawScansDirectory(_workDir), "scan_0002.png");
        using (var scan = new Mat(200, 200, MatType.CV_8UC3)) { scan.SetTo(new Scalar(230, 230, 230)); Cv2.ImWrite(emptyScan, scan); }
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

    [Fact]
    public async Task ScanStage_Discard_ShouldNeedASecondClickOnTheSameCard()
    {
        var vm = new ScanStageViewModel(_host);
        var row = new ScanRowViewModel(2, "scan.png", "1 photos", false);
        vm.Scans.Add(row);

        await vm.DiscardCommand.ExecuteAsync(row);
        row.IsConfirmingDiscard.ShouldBeTrue();
        _host.Discarded.ShouldBeEmpty();

        await vm.DiscardCommand.ExecuteAsync(row);
        _host.Discarded.ShouldBe([2]);
    }

    [Fact]
    public async Task ScanStage_DiscardConfirmation_ShouldMoveToTheLastClickedCard()
    {
        var vm = new ScanStageViewModel(_host);
        var first = new ScanRowViewModel(0, "a.png", "", false);
        var second = new ScanRowViewModel(1, "b.png", "", false);
        vm.Scans.Add(first);
        vm.Scans.Add(second);

        await vm.DiscardCommand.ExecuteAsync(first);
        await vm.DiscardCommand.ExecuteAsync(second);

        first.IsConfirmingDiscard.ShouldBeFalse();
        second.IsConfirmingDiscard.ShouldBeTrue();
        _host.Discarded.ShouldBeEmpty("each card needs its own second click");
    }

    [Fact]
    public async Task ScanStage_Rescan_ShouldAskTheHostToRescanThatScan()
    {
        var vm = new ScanStageViewModel(_host);
        var row = new ScanRowViewModel(4, "scan.png", "", false);
        row.IsConfirmingDiscard = true;
        vm.Scans.Add(row);

        await vm.RescanCommand.ExecuteAsync(row);

        _host.Rescanned.ShouldBe([4]);
        row.IsConfirmingDiscard.ShouldBeFalse();
    }

    [Fact]
    public void ScanStage_Refresh_ShouldLoadScanThumbnails()
    {
        string scanPath = Path.Combine(ProjectWorkspaceService.GetRawScansDirectory(_workDir), "scan_0009.png");
        using (var scan = new Mat(300, 400, MatType.CV_8UC3)) { scan.SetTo(new Scalar(200, 180, 160)); Cv2.ImWrite(scanPath, scan); }
        _host.ScanItems.Add(new ScanSessionItem(scanPath, new DetectionOptions()));
        var vm = new ScanStageViewModel(_host);

        vm.Refresh();

        SpinWait.SpinUntil(() => vm.Scans[0].Thumbnail != null, TimeSpan.FromSeconds(5)).ShouldBeTrue();
        vm.Scans[0].Thumbnail!.PixelSize.Width.ShouldBe(ScanStageViewModel.ThumbnailWidth);
    }

    private string AddScan(string name, int width = 400, int height = 300)
    {
        string scanPath = Path.Combine(ProjectWorkspaceService.GetRawScansDirectory(_workDir), name);
        using (var scan = new Mat(height, width, MatType.CV_8UC3)) { scan.SetTo(new Scalar(200, 180, 160)); Cv2.ImWrite(scanPath, scan); }
        _host.ScanItems.Add(new ScanSessionItem(scanPath, new DetectionOptions()));
        return scanPath;
    }

    [Fact]
    public void ScanStage_Refresh_ShouldSelectANewScanAsSoonAsItArrives()
    {
        AddScan("scan_0001.png");
        AddScan("scan_0002.png");
        var vm = new ScanStageViewModel(_host);

        vm.Refresh();
        vm.SelectedScan!.FileName.ShouldBe("scan_0001.png");

        vm.SelectedScan = vm.Scans[1];
        vm.Refresh();
        vm.SelectedScan!.FileName.ShouldBe("scan_0002.png"); // same scan after a rebuild

        AddScan("scan_0003.png");
        vm.Refresh();
        vm.SelectedScan!.FileName.ShouldBe("scan_0003.png"); // the one just scanned

        _host.ScanItems[2].Dispose();
        _host.ScanItems.RemoveAt(2);
        vm.Refresh();
        vm.SelectedScan!.FileName.ShouldBe("scan_0002.png"); // a discarded scan's place goes to its neighbour
    }

    [Fact]
    public void ScanStage_Preview_ShouldOutlineSavedBoxesWithoutDetecting()
    {
        string path = AddScan("scan_0001.png", width: 400, height: 200);
        _host.Workspace!.Scans.Add(new WorkspaceScanEntry
        {
            RelativePath = Path.GetRelativePath(_workDir, path),
            FinalCrops = { new WorkspaceCropData { CenterX = 100, CenterY = 50, Width = 100, Height = 50 } }
        });
        var vm = new ScanStageViewModel(_host);

        vm.Refresh();

        SpinWait.SpinUntil(() => vm.PreviewBoxes != null, TimeSpan.FromSeconds(5)).ShouldBeTrue();
        vm.Preview.ShouldNotBeNull();
        _host.PrepareCalls.ShouldBe(0);
        var corners = vm.PreviewBoxes!.ShouldHaveSingleItem();
        corners.Min(p => p.X).ShouldBe(0.125, 0.01); // (100 - 50) / 400
        corners.Max(p => p.X).ShouldBe(0.375, 0.01);
        corners.Min(p => p.Y).ShouldBe(0.125, 0.01); // (50 - 25) / 200
        corners.Max(p => p.Y).ShouldBe(0.375, 0.01);
        vm.Scans[0].PhotosText.ShouldBe(LocalizationService.Format(ResourceKeys.ScanStatusPhotos, "{0} photos", 1));
    }

    [Fact]
    public void ScanStage_Preview_ShouldAskTheCropsStageToFindPhotosOnANewScan()
    {
        AddScan("scan_0001.png");
        _host.PreparedBoxes[0] = [new WorkspaceCropData { CenterX = 200, CenterY = 150, Width = 80, Height = 60 }, new WorkspaceCropData { CenterX = 60, CenterY = 60, Width = 40, Height = 40 }];
        var vm = new ScanStageViewModel(_host);

        vm.Refresh();

        SpinWait.SpinUntil(() => vm.PreviewBoxes != null, TimeSpan.FromSeconds(5)).ShouldBeTrue();
        _host.PrepareCalls.ShouldBe(1);
        vm.PreviewBoxes!.Count.ShouldBe(2);
        vm.Scans[0].PhotosText.ShouldBe(LocalizationService.Format(ResourceKeys.ScanStatusPhotos, "{0} photos", 2));
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

        public List<int> Discarded { get; } = [];
        public List<int> Rescanned { get; } = [];

        public Task ScanAsync() => Task.CompletedTask;
        public Task ConfigureScannerAsync() => Task.CompletedTask;

        /// <summary>Boxes PrepareScanAsync returns, by scan index; how often it was called.</summary>
        public Dictionary<int, IReadOnlyList<WorkspaceCropData>> PreparedBoxes { get; } = [];
        public int PrepareCalls { get; private set; }

        public Task<IReadOnlyList<WorkspaceCropData>> PrepareScanAsync(int scanIndex, CancellationToken ct)
        {
            PrepareCalls++;
            return Task.FromResult(PreparedBoxes.TryGetValue(scanIndex, out var boxes) ? boxes : (IReadOnlyList<WorkspaceCropData>)[]);
        }
        public Task ImportScansAsync() => Task.CompletedTask;
        public void OpenScanInCrops(int scanIndex) => OpenedScan = scanIndex;
        public Task DiscardScanAsync(int scanIndex) { Discarded.Add(scanIndex); return Task.CompletedTask; }
        public Task RescanAsync(int scanIndex) { Rescanned.Add(scanIndex); return Task.CompletedTask; }
        public Task<IReadOnlyList<string>> PickPhotosToImportAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public FinalRenderOptions GetRenderOptions() => new();
        public async Task<bool> RunBusyAsync(string message, Func<Task> work) { await work(); return true; }
        public void SaveWorkspace() => Saves++;
        public void ShowStatus(string message) { }
        public List<(string Message, bool Succeeded)> Notified { get; } = [];
        public void NotifyExportCompleted(string message, bool succeeded) => Notified.Add((message, succeeded));
        public void OpenFolder(string path) { }
    }
}
