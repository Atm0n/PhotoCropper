using OpenCvSharp;
using PhotoCropper.Core.Export;
using PhotoCropper.Core.Workspace;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Core.Tests.Workspace;

public sealed class PhotoPipelineServiceTests : IDisposable
{
    private readonly string _workDir;
    private readonly WorkspaceSessionState _session = WorkspaceSessionState.CreateNew();
    private readonly WorkspaceScanEntry _scan;

    public PhotoPipelineServiceTests()
    {
        _workDir = TestImageFactory.CreateTempDirectory("PhotoPipelineTests");
        ProjectWorkspaceService.InitializeWorkspace(_workDir);
        string scanPath = Path.Combine(ProjectWorkspaceService.GetRawScansDirectory(_workDir), "scan_0001.png");
        using (var scan = new Mat(400, 400, MatType.CV_8UC3))
        {
            scan.SetTo(new Scalar(230, 230, 230));
            Cv2.ImWrite(scanPath, scan);
        }
        _scan = new WorkspaceScanEntry { RelativePath = Path.GetRelativePath(_workDir, scanPath), OriginalFileName = "scan_0001.png" };
        _session.Scans.Add(_scan);
    }

    private static Mat Photo(double shade, int width = 60, int height = 40)
    {
        var mat = new Mat(height, width, MatType.CV_8UC3);
        mat.SetTo(new Scalar(shade, shade * 0.8, shade * 0.6));
        return mat;
    }

    private static WorkspaceCropData Box(float cx, float cy, float w = 60, float h = 40) =>
        new() { CenterX = cx, CenterY = cy, Width = w, Height = h };

    private IReadOnlyList<ProjectPhoto> Save(params (Mat Image, WorkspaceCropData Crop)[] photos) =>
        PhotoPipelineService.SaveCroppedPhotos(_workDir, _session, _scan,
            [.. photos.Select(p => new CroppedPhotoInput(p.Image, p.Crop))], RestorationSettings.None);

    private string Full(ProjectFile file) => file.GetFullPath(_workDir);

    [Fact]
    public void SaveCroppedPhotos_ShouldAssignStableIdsAndWriteLosslessPngsToCropped()
    {
        using var a = Photo(100);
        using var b = Photo(150);

        var photos = Save((a, Box(100, 100)), (b, Box(300, 300)));

        photos.Select(p => p.Id).ShouldBe(["scan_0001_p01", "scan_0001_p02"]);
        foreach (var photo in photos)
        {
            Path.GetDirectoryName(Full(photo.Cropped!)).ShouldBe(ProjectWorkspaceService.GetCroppedDirectory(_workDir));
            Path.GetExtension(photo.Cropped!.RelativePath).ShouldBe(".png");
            File.Exists(Full(photo.Cropped)).ShouldBeTrue();
        }
    }

    [Fact]
    public void SaveCroppedPhotos_ReCropWithSlightlyMovedBox_ShouldKeepTheIdAndSettings()
    {
        using var a = Photo(100);
        var first = Save((a, Box(100, 100)))[0];
        first.Restoration = new RestorationSettings { RestoreColors = true };

        using var moved = Photo(110);
        var second = Save((moved, Box(104, 102)))[0];

        second.Id.ShouldBe(first.Id);
        second.Restoration.RestoreColors.ShouldBeTrue();
        second.Crop!.CenterX.ShouldBe(104);
    }

    [Fact]
    public void SaveCroppedPhotos_UnchangedImage_ShouldNotRewriteTheFile()
    {
        using var a = Photo(100);
        var first = Save((a, Box(100, 100)))[0];
        var before = first.Cropped!;

        var again = Save((a, Box(100, 100)))[0];

        again.Cropped!.IsSameVersionAs(before).ShouldBeTrue();
    }

    [Fact]
    public void SaveCroppedPhotos_RemovedAndNewBoxes_ShouldDeleteOldFilesAndNeverReuseNumbers()
    {
        using var a = Photo(100);
        using var b = Photo(150);
        var initial = Save((a, Box(100, 100)), (b, Box(300, 300)));
        string removedFile = Full(initial[1].Cropped!);

        using var c = Photo(200);
        var updated = Save((a, Box(100, 100)), (c, Box(200, 50)));

        updated.Select(p => p.Id).ShouldBe(["scan_0001_p01", "scan_0001_p03"]);
        File.Exists(removedFile).ShouldBeFalse();
        _session.Photos.Count.ShouldBe(2);
    }

    [Fact]
    public void RenderFinal_ShouldWriteFinalWithPatternAndClearStaleness()
    {
        using var a = Photo(100);
        var photo = Save((a, Box(100, 100)))[0];
        photo.IsFinalStale.ShouldBeTrue();

        PhotoPipelineService.RenderFinal(_workDir, _session, photo, new FinalRenderOptions());

        Path.GetFileName(Full(photo.Final!)).ShouldBe("scan_0001_1.jpg");
        Path.GetDirectoryName(Full(photo.Final!)).ShouldBe(ProjectWorkspaceService.GetFinalDirectory(_workDir));
        photo.IsFinalStale.ShouldBeFalse();
    }

    [Fact]
    public void IsFinalStale_ShouldTurnTrueWhenRestorationOrCroppedPhotoChanges()
    {
        using var a = Photo(100);
        var photo = Save((a, Box(100, 100)))[0];
        PhotoPipelineService.RenderFinal(_workDir, _session, photo, new FinalRenderOptions());

        photo.Restoration = new RestorationSettings { RestoreColors = true };
        photo.IsFinalStale.ShouldBeTrue();
        PhotoPipelineService.RenderFinal(_workDir, _session, photo, new FinalRenderOptions());
        photo.IsFinalStale.ShouldBeFalse();

        using var changed = Photo(180);
        Save((changed, Box(100, 100)));
        photo.IsFinalStale.ShouldBeTrue();
    }

    [Fact]
    public void RenderFinal_AgainAfterChange_ShouldReplaceThePreviousFinalFile()
    {
        using var a = Photo(100);
        var photo = Save((a, Box(100, 100)))[0];
        PhotoPipelineService.RenderFinal(_workDir, _session, photo, new FinalRenderOptions());

        PhotoPipelineService.RenderFinal(_workDir, _session, photo, new FinalRenderOptions { Format = "PNG" });

        Directory.GetFiles(ProjectWorkspaceService.GetFinalDirectory(_workDir)).Select(Path.GetFileName)
            .ShouldBe(["scan_0001_1.png"]);
    }

    [Fact]
    public void ImportPhoto_ShouldCopyIntoCroppedAndRenderWithItsOwnName()
    {
        string source = Path.Combine(_workDir, "grandma.jpg");
        using (var img = Photo(120)) Cv2.ImWrite(source, img);

        var photo = PhotoPipelineService.ImportPhoto(_workDir, _session, source, RestorationSettings.None);
        PhotoPipelineService.RenderFinal(_workDir, _session, photo, new FinalRenderOptions { FileNamePattern = "{original}" });

        photo.Id.ShouldBe("import_0001");
        photo.ScanRelativePath.ShouldBeNull();
        Path.GetFileName(Full(photo.Cropped!)).ShouldBe("import_0001.png");
        Path.GetFileName(Full(photo.Final!)).ShouldBe("grandma.jpg");
    }

    [Fact]
    public void RemoveScanPhotos_ShouldDropRecordsAndPipelineFiles()
    {
        using var a = Photo(100);
        var photo = Save((a, Box(100, 100)))[0];
        PhotoPipelineService.RenderFinal(_workDir, _session, photo, new FinalRenderOptions());
        string cropped = Full(photo.Cropped!);
        string final = Full(photo.Final!);

        int removed = PhotoPipelineService.RemoveScanPhotos(_workDir, _session, _scan.RelativePath);

        removed.ShouldBe(1);
        _session.Photos.ShouldBeEmpty();
        File.Exists(cropped).ShouldBeFalse();
        File.Exists(final).ShouldBeFalse();
    }

    [Fact]
    public void RemoveScanPhotos_ShouldKeepAFinalFileChangedOutsideTheApp()
    {
        using var a = Photo(100);
        var photo = Save((a, Box(100, 100)))[0];
        PhotoPipelineService.RenderFinal(_workDir, _session, photo, new FinalRenderOptions());
        string final = Full(photo.Final!);
        File.WriteAllText(final, "edited by the user");

        PhotoPipelineService.RemoveScanPhotos(_workDir, _session, _scan.RelativePath);

        File.Exists(final).ShouldBeTrue();
    }

    [Theory]
    [InlineData(100, 100, 1.0)]
    [InlineData(400, 400, 0.0)]
    public void BoxOverlap_ShouldMeasureIntersectionOverUnion(float cx, float cy, double expected)
    {
        PhotoPipelineService.BoxOverlap(Box(100, 100), Box(cx, cy)).ShouldBe(expected, 0.001);
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
