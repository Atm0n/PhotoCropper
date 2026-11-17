using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Core.Extraction;
using PhotoCropper.Core.Workspace;
using PhotoCropper.TestHelpers;
using System.Drawing;

namespace PhotoCropper.Core.Tests.Extraction;

public sealed class RepairBrushTests : IDisposable
{
    private readonly string _workDir = TestImageFactory.CreateTempDirectory("RepairBrushTests");

    /// <summary>A grey photo with a long dark scratch across it (too long for automatic dust removal).</summary>
    private static Mat ScratchedPhoto(int width = 200, int height = 100, int channels = 3)
    {
        var mat = new Mat(height, width, DepthType.Cv8U, channels);
        mat.SetTo(channels == 4 ? new MCvScalar(128, 128, 128, 200) : new MCvScalar(128, 128, 128));
        CvInvoke.Line(mat, new Point(20, 50), new Point(180, 50), channels == 4 ? new MCvScalar(20, 20, 20, 200) : new MCvScalar(20, 20, 20), 2);
        return mat;
    }

    /// <summary>A stroke along the scratch, in fractions of the photo's size.</summary>
    private static RepairStroke StrokeAlongScratch(float radius = 0.02f)
    {
        var stroke = new RepairStroke { Radius = radius };
        foreach (float v in new[] { 0.08f, 0.5f, 0.92f, 0.5f }) stroke.Points.Add(v);
        return stroke;
    }

    private static double MeanAt(Mat image, Rectangle area)
    {
        using var region = new Mat(image, area);
        return CvInvoke.Mean(region).V0;
    }

    [Fact]
    public void CreateMask_ShouldCoverTheStrokeOnly()
    {
        using var mask = PhotoRepairService.CreateMask(new Size(200, 100), [StrokeAlongScratch()]);

        MeanAt(mask, new Rectangle(40, 49, 120, 3)).ShouldBe(255, "under the stroke");
        MeanAt(mask, new Rectangle(40, 10, 120, 20)).ShouldBe(0, "away from the stroke");
    }

    [Fact]
    public void ApplyRepairs_ShouldFillInAPaintedOverScratch()
    {
        using var photo = ScratchedPhoto();

        using var repaired = PhotoRepairService.ApplyRepairs(photo, [StrokeAlongScratch()]);

        Math.Abs(MeanAt(repaired, new Rectangle(40, 49, 120, 3)) - 128).ShouldBeLessThan(6);
        Math.Abs(MeanAt(photo, new Rectangle(40, 49, 120, 3)) - 128).ShouldBeGreaterThan(40, "the original is untouched");
    }

    /// <summary>A photo with film-like grain and a long scratch.</summary>
    private static Mat GrainyScratchedPhoto(out Mat clean)
    {
        clean = new Mat(300, 400, DepthType.Cv8U, 3);
        clean.SetTo(new MCvScalar(120, 130, 140));
        using (var plus = new Mat(300, 400, DepthType.Cv8U, 3))
        using (var minus = new Mat(300, 400, DepthType.Cv8U, 3))
        {
            CvInvoke.Randn(plus, new MCvScalar(0, 0, 0), new MCvScalar(14, 14, 14));
            CvInvoke.Randn(minus, new MCvScalar(0, 0, 0), new MCvScalar(14, 14, 14));
            CvInvoke.Add(clean, plus, clean);
            CvInvoke.Subtract(clean, minus, clean);
        }
        var damaged = clean.Clone();
        CvInvoke.Line(damaged, new Point(40, 150), new Point(360, 150), new MCvScalar(20, 20, 20), 6);
        return damaged;
    }

    private static RepairStroke HorizontalStroke(float y, float radius)
    {
        var stroke = new RepairStroke { Radius = radius };
        foreach (float v in new[] { 0.1f, y, 0.9f, y }) stroke.Points.Add(v);
        return stroke;
    }

    [Fact]
    public void ApplyRepairs_OnAGrainyPhoto_ShouldMatchTheSurroundingGrainInsteadOfLookingBlurred()
    {
        using var damaged = GrainyScratchedPhoto(out var clean);
        using (clean)
        {
            using var repaired = PhotoRepairService.ApplyRepairs(damaged, [HorizontalStroke(0.5f, 0.0125f)]);

            var fillMean = new MCvScalar(); var fillStd = new MCvScalar();
            var refMean = new MCvScalar(); var refStd = new MCvScalar();
            using (var fill = new Mat(repaired, new Rectangle(60, 147, 280, 6))) CvInvoke.MeanStdDev(fill, ref fillMean, ref fillStd);
            using (var reference = new Mat(clean, new Rectangle(60, 100, 280, 6))) CvInvoke.MeanStdDev(reference, ref refMean, ref refStd);

            // A plain blend keeps only ~15% of the texture; the grain should be close to the surroundings.
            (fillStd.V0 / refStd.V0).ShouldBeInRange(0.7, 1.3);
            Math.Abs(fillMean.V0 - refMean.V0).ShouldBeLessThan(5);
        }
    }

    [Fact]
    public void ApplyRepairs_OnASmoothPhoto_ShouldNotAddGrain()
    {
        using var photo = ScratchedPhoto(); // flat grey: no grain around the scratch

        using var repaired = PhotoRepairService.ApplyRepairs(photo, [StrokeAlongScratch()]);

        var mean = new MCvScalar(); var std = new MCvScalar();
        using var fill = new Mat(repaired, new Rectangle(40, 47, 120, 6));
        CvInvoke.MeanStdDev(fill, ref mean, ref std);
        std.V0.ShouldBeLessThan(2);
    }

    [Fact]
    public void ApplyRepairs_ShouldGiveTheSameResultEveryTime()
    {
        using var damaged = GrainyScratchedPhoto(out var clean);
        using (clean)
        {
            using var first = PhotoRepairService.ApplyRepairs(damaged, [HorizontalStroke(0.5f, 0.0125f)]);
            using var second = PhotoRepairService.ApplyRepairs(damaged, [HorizontalStroke(0.5f, 0.0125f)]);

            CvInvoke.Norm(first, second, NormType.L1).ShouldBe(0);
        }
    }

    [Fact]
    public void ApplyRepairs_ShouldKeepTransparency()
    {
        using var photo = ScratchedPhoto(channels: 4);

        using var repaired = PhotoRepairService.ApplyRepairs(photo, [StrokeAlongScratch()]);

        repaired.NumberOfChannels.ShouldBe(4);
        using var channels = new Emgu.CV.Util.VectorOfMat();
        CvInvoke.Split(repaired, channels);
        CvInvoke.Mean(channels[3]).V0.ShouldBe(200);
    }

    [Fact]
    public void ApplyRepairs_WithoutStrokes_ShouldReturnTheSameImage()
    {
        using var photo = ScratchedPhoto();

        PhotoRepairService.ApplyRepairs(photo, []).ShouldBeSameAs(photo);
    }

    private (WorkspaceSessionState Session, ProjectPhoto Photo) SavePhoto(Mat image)
    {
        ProjectWorkspaceService.InitializeWorkspace(_workDir);
        string scanPath = Path.Combine(ProjectWorkspaceService.GetRawScansDirectory(_workDir), "scan_0001.png");
        using (var scan = new Mat(400, 400, DepthType.Cv8U, 3)) { scan.SetTo(new MCvScalar(230, 230, 230)); scan.Save(scanPath); }
        var session = WorkspaceSessionState.CreateNew();
        var entry = new WorkspaceScanEntry { RelativePath = Path.GetRelativePath(_workDir, scanPath) };
        session.Scans.Add(entry);
        var crop = new WorkspaceCropData { CenterX = 100, CenterY = 100, Width = image.Width, Height = image.Height };
        var photo = PhotoPipelineService.SaveCroppedPhotos(_workDir, session, entry, [new CroppedPhotoInput(image, crop)], RestorationSettings.None)[0];
        return (session, photo);
    }

    [Fact]
    public void RenderFinal_ShouldApplyRepairsAndTrackThemForStaleness()
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
        Math.Abs(MeanAt(final, new Rectangle(40, 49, 120, 3)) - 128).ShouldBeLessThan(6);
    }

    [Fact]
    public void RenderFinal_WithRepairsFromAnEarlierCropSize_ShouldNotApplyThem()
    {
        using var scratched = ScratchedPhoto();
        var (session, photo) = SavePhoto(scratched);
        photo.RepairImageWidth = 300; // painted on a crop of another size
        photo.RepairImageHeight = 100;
        photo.Repairs.Add(StrokeAlongScratch());

        PhotoPipelineService.RenderFinal(_workDir, session, photo, new FinalRenderOptions { Format = "PNG" });

        photo.AreRepairsOutdated(200, 100).ShouldBeTrue();
        using var final = PhotoCropper.Core.Export.RestoreStage.ReadImage(photo.Final!.GetFullPath(_workDir));
        Math.Abs(MeanAt(final, new Rectangle(40, 49, 120, 3)) - 128).ShouldBeGreaterThan(40, "misaligned strokes are not applied");
        photo.IsFinalStale.ShouldBeFalse("it was rendered with these (unapplied) strokes");
    }

    [Fact]
    public void Repairs_ShouldSurviveSavingAndLoadingTheSession()
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
