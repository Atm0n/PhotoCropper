using OpenCvSharp;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Extraction;
using PhotoCropper.Core.Workspace;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Core.Tests.Extraction;

public sealed class RepairBrushTests : IDisposable
{
    private readonly string _workDir = TestImageFactory.CreateTempDirectory("RepairBrushTests");

    /// <summary>A grey photo with a long dark scratch across it (too long for automatic dust removal).</summary>
    private static Mat ScratchedPhoto(int width = 200, int height = 100, int channels = 3)
    {
        var mat = new Mat(height, width, MatType.CV_8UC(channels));
        mat.SetTo(channels == 4 ? new Scalar(128, 128, 128, 200) : new Scalar(128, 128, 128));
        Cv2.Line(mat, new Point(20, 50), new Point(180, 50), channels == 4 ? new Scalar(20, 20, 20, 200) : new Scalar(20, 20, 20), 2);
        return mat;
    }

    /// <summary>A stroke along the scratch, in fractions of the photo's size.</summary>
    private static RepairStroke StrokeAlongScratch(float radius = 0.02f)
    {
        var stroke = new RepairStroke { Radius = radius };
        foreach (float v in new[] { 0.08f, 0.5f, 0.92f, 0.5f }) stroke.Points.Add(v);
        return stroke;
    }

    private static double MeanAt(Mat image, Rect area)
    {
        using var region = new Mat(image, area);
        return Cv2.Mean(region).Val0;
    }

    [Fact]
    public void CreateMaskShouldCoverTheStrokeOnly()
    {
        using var mask = PhotoRepairService.CreateMask(new Size(200, 100), [StrokeAlongScratch()]);

        MeanAt(mask, new Rect(40, 49, 120, 3)).ShouldBe(255, "under the stroke");
        MeanAt(mask, new Rect(40, 10, 120, 20)).ShouldBe(0, "away from the stroke");
    }

    [Fact]
    public void ApplyRepairsShouldFillInAPaintedOverScratch()
    {
        using var photo = ScratchedPhoto();

        using var repaired = PhotoRepairService.ApplyRepairs(photo, [StrokeAlongScratch()]);

        Math.Abs(MeanAt(repaired, new Rect(40, 49, 120, 3)) - 128).ShouldBeLessThan(6);
        Math.Abs(MeanAt(photo, new Rect(40, 49, 120, 3)) - 128).ShouldBeGreaterThan(40, "the original is untouched");
    }

    /// <summary>A photo with film-like (Gaussian) grain and a long scratch.</summary>
    private static Mat GrainyScratchedPhoto(out Mat clean)
    {
        // Gaussian noise centred on the photo's colours, std 14, well inside 0..255 so nothing is clipped.
        clean = new Mat(300, 400, MatType.CV_8UC3);
        Cv2.Randn(clean, new Scalar(120, 130, 140), new Scalar(14, 14, 14));
        var damaged = clean.Clone();
        Cv2.Line(damaged, new Point(40, 150), new Point(360, 150), new Scalar(20, 20, 20), 6);
        return damaged;
    }

    private static RepairStroke HorizontalStroke(float y, float radius)
    {
        var stroke = new RepairStroke { Radius = radius };
        foreach (float v in new[] { 0.1f, y, 0.9f, y }) stroke.Points.Add(v);
        return stroke;
    }

    [Fact]
    public void ApplyRepairsOnAGrainyPhotoShouldMatchTheSurroundingGrainInsteadOfLookingBlurred()
    {
        using var damaged = GrainyScratchedPhoto(out var clean);
        using (clean)
        {
            using var repaired = PhotoRepairService.ApplyRepairs(damaged, [HorizontalStroke(0.5f, 0.0125f)]);

            var fillMean = new Scalar(); var fillStd = new Scalar();
            var refMean = new Scalar(); var refStd = new Scalar();
            using (var fill = new Mat(repaired, new Rect(60, 147, 280, 6))) Cv2.MeanStdDev(fill, out fillMean, out fillStd);
            using (var reference = new Mat(clean, new Rect(60, 100, 280, 6))) Cv2.MeanStdDev(reference, out refMean, out refStd);

            // A plain blend keeps only ~15% of the texture; the grain should be close to the surroundings.
            (fillStd.Val0 / refStd.Val0).ShouldBeInRange(0.7, 1.3);
            Math.Abs(fillMean.Val0 - refMean.Val0).ShouldBeLessThan(5);
        }
    }

    [Fact]
    public void ApplyRepairsOnASmoothPhotoShouldNotAddGrain()
    {
        using var photo = ScratchedPhoto(); // flat grey: no grain around the scratch

        using var repaired = PhotoRepairService.ApplyRepairs(photo, [StrokeAlongScratch()]);

        var mean = new Scalar(); var std = new Scalar();
        using var fill = new Mat(repaired, new Rect(40, 47, 120, 6));
        Cv2.MeanStdDev(fill, out mean, out std);
        std.Val0.ShouldBeLessThan(2);
    }

    [Fact]
    public void ApplyRepairsShouldGiveTheSameResultEveryTime()
    {
        using var damaged = GrainyScratchedPhoto(out var clean);
        using (clean)
        {
            using var first = PhotoRepairService.ApplyRepairs(damaged, [HorizontalStroke(0.5f, 0.0125f)]);
            using var second = PhotoRepairService.ApplyRepairs(damaged, [HorizontalStroke(0.5f, 0.0125f)]);

            Cv2.Norm(first, second, NormTypes.L1).ShouldBe(0);
        }
    }

    [Fact]
    public void ApplyRepairsShouldKeepTransparency()
    {
        using var photo = ScratchedPhoto(channels: 4);

        using var repaired = PhotoRepairService.ApplyRepairs(photo, [StrokeAlongScratch()]);

        repaired.Channels().ShouldBe(4);
        using var channels = MatChannels.Split(repaired);
        Cv2.Mean(channels[3]).Val0.ShouldBe(200);
    }

    [Fact]
    public void ApplyRepairsWithoutStrokesShouldReturnTheSameImage()
    {
        using var photo = ScratchedPhoto();

        PhotoRepairService.ApplyRepairs(photo, []).ShouldBeSameAs(photo);
    }

    private (WorkspaceSessionState Session, ProjectPhoto Photo) SavePhoto(Mat image)
    {
        ProjectWorkspaceService.InitializeWorkspace(_workDir);
        string scanPath = Path.Combine(ProjectWorkspaceService.GetRawScansDirectory(_workDir), "scan_0001.png");
        using (var scan = new Mat(400, 400, MatType.CV_8UC3)) { scan.SetTo(new Scalar(230, 230, 230)); Cv2.ImWrite(scanPath, scan); }
        var session = WorkspaceSessionState.CreateNew();
        var entry = new WorkspaceScanEntry { RelativePath = Path.GetRelativePath(_workDir, scanPath) };
        session.Scans.Add(entry);
        var crop = new WorkspaceCropData { CenterX = 100, CenterY = 100, Width = image.Width, Height = image.Height };
        var photo = PhotoPipelineService.SaveCroppedPhotos(_workDir, session, entry, [new CroppedPhotoInput(image, crop)], RestorationSettings.None)[0];
        return (session, photo);
    }

    [Fact]
    public void RenderFinalShouldApplyRepairsAndTrackThemForStaleness()
    {
        using var scratched = ScratchedPhoto();
        var (session, photo) = SavePhoto(scratched);
        var options = new FinalRenderOptions { Format = "PNG" };
        PhotoPipelineService.RenderFinal(_workDir, session, photo, options);
        photo.IsFinalStale.ShouldBeFalse();

        photo.RepairImageWidth = 200;
        photo.RepairImageHeight = 100;
        photo.Repairs.Add(StrokeAlongScratch());
        photo.IsFinalStale.ShouldBeTrue("adding a repair changes the final photo");

        PhotoPipelineService.RenderFinal(_workDir, session, photo, options);

        photo.IsFinalStale.ShouldBeFalse();
        using var final = PhotoCropper.Core.Export.RestoreStage.ReadImage(photo.Final!.GetFullPath(_workDir));
        Math.Abs(MeanAt(final, new Rect(40, 49, 120, 3)) - 128).ShouldBeLessThan(6);
    }

    [Fact]
    public void RenderFinalWithRepairsFromAnEarlierCropSizeShouldNotApplyThem()
    {
        using var scratched = ScratchedPhoto();
        var (session, photo) = SavePhoto(scratched);
        photo.RepairImageWidth = 300; // painted on a crop of another size
        photo.RepairImageHeight = 100;
        photo.Repairs.Add(StrokeAlongScratch());

        PhotoPipelineService.RenderFinal(_workDir, session, photo, new FinalRenderOptions { Format = "PNG" });

        photo.AreRepairsOutdated(200, 100).ShouldBeTrue();
        using var final = PhotoCropper.Core.Export.RestoreStage.ReadImage(photo.Final!.GetFullPath(_workDir));
        Math.Abs(MeanAt(final, new Rect(40, 49, 120, 3)) - 128).ShouldBeGreaterThan(40, "misaligned strokes are not applied");
        photo.IsFinalStale.ShouldBeFalse("it was rendered with these (unapplied) strokes");
    }

    [Fact]
    public void RepairsShouldSurviveSavingAndLoadingTheSession()
    {
        using var scratched = ScratchedPhoto();
        var (session, photo) = SavePhoto(scratched);
        photo.RepairImageWidth = 200;
        photo.RepairImageHeight = 100;
        photo.Repairs.Add(StrokeAlongScratch(0.015f));
        string fingerprint = photo.RepairsFingerprint;

        ProjectWorkspaceService.SaveSession(_workDir, session);
        var loaded = ProjectWorkspaceService.LoadSession(_workDir)!.Photos.Single();

        loaded.Repairs.Count.ShouldBe(1);
        loaded.Repairs[0].Radius.ShouldBe(0.015f);
        loaded.Repairs[0].Points.ShouldBe([0.08f, 0.5f, 0.92f, 0.5f]);
        loaded.RepairsFingerprint.ShouldBe(fingerprint);
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
