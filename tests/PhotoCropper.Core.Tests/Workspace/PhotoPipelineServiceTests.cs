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
    public void SaveCroppedPhotosShouldAssignStableIdsAndWriteLosslessPngsToCropped()
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
    public void SaveCroppedPhotosReCropWithSlightlyMovedBoxShouldKeepTheIdAndSettings()
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
    public void SaveCroppedPhotosUnchangedImageShouldNotRewriteTheFile()
    {
        using var a = Photo(100);
        var first = Save((a, Box(100, 100)))[0];
        var before = first.Cropped!;

        var again = Save((a, Box(100, 100)))[0];

        again.Cropped!.IsSameVersionAs(before).ShouldBeTrue();
    }

    [Fact]
    public void SaveCroppedPhotosRemovedAndNewBoxesShouldDeleteOldFilesAndNeverReuseNumbers()
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
    public void RenderFinalShouldWriteFinalWithPatternAndClearStaleness()
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
    public void RenderFinalsShouldWriteEveryPhotoUnderTheNameARenderOneByOneGives()
    {
        var images = Enumerable.Range(0, 2 * Environment.ProcessorCount + 1).Select(i => Photo(40 + i)).ToList();
        try
        {
            var photos = Save([.. images.Select((m, i) => (m, Box(20 + i * 3, 20 + i * 3, 10, 10)))]);

            int written = PhotoPipelineService.RenderFinals(_workDir, _session, photos, new FinalRenderOptions(), onFailure: null);

            written.ShouldBe(photos.Count);
            photos.Select(p => Path.GetFileName(Full(p.Final!))).ShouldBe(photos.Select(p => $"scan_0001_{p.Number}.jpg"));
            photos.ShouldAllBe(p => !p.IsFinalStale);
        }
        finally
        {
            foreach (var image in images) image.Dispose();
        }
    }

    [Fact]
    public void RenderFinalsWithAMissingCroppedFileShouldReportItAndWriteTheOthers()
    {
        using var a = Photo(100);
        using var b = Photo(150);
        using var c = Photo(200);
        var photos = Save((a, Box(100, 100)), (b, Box(300, 300)), (c, Box(200, 50)));
        File.Delete(Full(photos[1].Cropped!));
        var failed = new List<ProjectPhoto>();

        int written = PhotoPipelineService.RenderFinals(_workDir, _session, photos, new FinalRenderOptions(), (photo, _) => failed.Add(photo));

        written.ShouldBe(2);
        failed.ShouldBe([photos[1]]);
        photos[0].Final.ShouldNotBeNull();
        photos[1].Final.ShouldBeNull();
        photos[2].Final.ShouldNotBeNull();
    }

    [Fact]
    public void RenderFinalsWithoutAFailureHandlerShouldThrowAfterWritingThePhotosBeforeIt()
    {
        using var a = Photo(100);
        using var b = Photo(150);
        using var c = Photo(200);
        var photos = Save((a, Box(100, 100)), (b, Box(300, 300)), (c, Box(200, 50)));
        File.Delete(Full(photos[1].Cropped!));

        Should.Throw<FileNotFoundException>(() => PhotoPipelineService.RenderFinals(_workDir, _session, photos, new FinalRenderOptions(), onFailure: null));

        photos[0].Final.ShouldNotBeNull();
        photos[2].Final.ShouldBeNull();
    }

    [Fact]
    public void IsFinalStaleShouldTurnTrueWhenRestorationOrCroppedPhotoChanges()
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
    public void RenderFinalAgainAfterChangeShouldReplaceThePreviousFinalFile()
    {
        using var a = Photo(100);
        var photo = Save((a, Box(100, 100)))[0];
        PhotoPipelineService.RenderFinal(_workDir, _session, photo, new FinalRenderOptions());

        PhotoPipelineService.RenderFinal(_workDir, _session, photo, new FinalRenderOptions { Format = "PNG" });

        Directory.GetFiles(ProjectWorkspaceService.GetFinalDirectory(_workDir)).Select(Path.GetFileName)
            .ShouldBe(["scan_0001_1.png"]);
    }

    [Fact]
    public void RenderFinalShouldReplaceFilesNoOtherPhotoOwnsInsteadOfWritingACopy()
    {
        // Left in Final/ by an older version, or by an export whose record was lost (e.g. two windows saving).
        string finalDir = ProjectWorkspaceService.GetFinalDirectory(_workDir);
        Directory.CreateDirectory(finalDir);
        File.WriteAllText(Path.Combine(finalDir, "scan_0001_1.jpg"), "old export");
        File.WriteAllText(Path.Combine(finalDir, "scan_0001_1 (1).jpg"), "lost export");
        File.WriteAllText(Path.Combine(finalDir, "scan_0001_10.jpg"), "another photo");
        using var a = Photo(100);
        var photo = Save((a, Box(100, 100)))[0];

        PhotoPipelineService.RenderFinal(_workDir, _session, photo, new FinalRenderOptions());

        Path.GetFileName(Full(photo.Final!)).ShouldBe("scan_0001_1.jpg");
        Directory.GetFiles(finalDir).Select(Path.GetFileName).Order().ShouldBe(["scan_0001_1.jpg", "scan_0001_10.jpg"]);
        File.ReadAllText(Path.Combine(finalDir, "scan_0001_10.jpg")).ShouldBe("another photo");
        new FileInfo(Full(photo.Final!)).Length.ShouldNotBe("old export".Length);
    }

    [Theory]
    [InlineData("scan_0001_1.jpg", true)]
    [InlineData("SCAN_0001_1.JPG", true)]
    [InlineData("scan_0001_1 (1).jpg", true)]
    [InlineData("scan_0001_1 (23).jpg", true)]
    [InlineData("scan_0001_10.jpg", false)]
    [InlineData("scan_0001_1 ().jpg", false)]
    [InlineData("scan_0001_1 (a).jpg", false)]
    [InlineData("scan_0001_1(1).jpg", false)]
    [InlineData("scan_0001_1 (1).png", false)]
    [InlineData("scan_0001.jpg", false)]
    public void IsNameOrNumberedCopyShouldMatchOnlyTheNameAndItsNumberedCopies(string name, bool expected) =>
        PhotoPipelineService.IsNameOrNumberedCopy(name, "scan_0001_1", ".jpg").ShouldBe(expected);

    [Fact]
    public void RenderFinalShouldReplaceItsOwnFileEvenIfItWasEdited()
    {
        using var a = Photo(100);
        var photo = Save((a, Box(100, 100)))[0];
        PhotoPipelineService.RenderFinal(_workDir, _session, photo, new FinalRenderOptions());
        File.WriteAllText(Full(photo.Final!), "edited outside the app");
        photo.Restoration = new RestorationSettings { RestoreColors = true };

        PhotoPipelineService.RenderFinal(_workDir, _session, photo, new FinalRenderOptions());

        Directory.GetFiles(ProjectWorkspaceService.GetFinalDirectory(_workDir)).Select(Path.GetFileName).ShouldBe(["scan_0001_1.jpg"]);
    }

    [Fact]
    public void RenderFinalShouldKeepAnotherPhotosFileUnderTheSameName()
    {
        using var a = Photo(100);
        using var b = Photo(150);
        var photos = Save((a, Box(100, 100)), (b, Box(300, 100)));
        var options = new FinalRenderOptions { FileNamePattern = "{original}" }; // both are "scan_0001.jpg"

        PhotoPipelineService.RenderFinal(_workDir, _session, photos[0], options);
        PhotoPipelineService.RenderFinal(_workDir, _session, photos[1], options);

        Path.GetFileName(Full(photos[0].Final!)).ShouldBe("scan_0001.jpg");
        Path.GetFileName(Full(photos[1].Final!)).ShouldBe("scan_0001 (1).jpg");
        File.Exists(Full(photos[0].Final!)).ShouldBeTrue();
    }

    [Fact]
    public void RenderFinalToAnotherFolderShouldNotMoveFilesThere()
    {
        string outside = Path.Combine(_workDir, "Elsewhere");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "scan_0001_1.jpg"), "the user's own file");
        using var a = Photo(100);
        var photo = Save((a, Box(100, 100)))[0];

        PhotoPipelineService.RenderFinal(_workDir, _session, photo, new FinalRenderOptions { OutputDirectory = outside });

        Path.GetFileName(Full(photo.Final!)).ShouldBe("scan_0001_1 (1).jpg");
        File.ReadAllText(Path.Combine(outside, "scan_0001_1.jpg")).ShouldBe("the user's own file");
    }

    [Fact]
    public void ImportPhotoShouldCopyIntoCroppedAndRenderWithItsOwnName()
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
    public void RemoveScanPhotosShouldDropRecordsAndPipelineFiles()
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
    public void RemoveScanPhotosShouldKeepAFinalFileChangedOutsideTheApp()
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
    public void BoxOverlapShouldMeasureIntersectionOverUnion(float cx, float cy, double expected)
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
