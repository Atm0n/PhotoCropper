using OpenCvSharp;
using PhotoCropper.Core.Export;
using PhotoCropper.Core.Workspace;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Core.Tests.Workspace;

/// <summary>Turning a photo in the Finish stage: its file, its box on the scan, and what is stored as fractions of it.</summary>
public sealed class RotatePhotoTests : IDisposable
{
    private readonly string _workDir = TestImageFactory.CreateTempDirectory("RotatePhotoTests");
    private readonly WorkspaceSessionState _session = WorkspaceSessionState.CreateNew();

    public void Dispose()
    {
        try { Directory.Delete(_workDir, true); } catch (IOException) { }
    }

    /// <summary>A project with one scan (the standard two-photo scan) whose photos are saved as the app does.</summary>
    private (WorkspaceScanEntry Scan, IReadOnlyList<ProjectPhoto> Photos) CreateProject()
    {
        ProjectWorkspaceService.InitializeWorkspace(_workDir);
        string scanPath = Path.Combine(ProjectWorkspaceService.GetRawScansDirectory(_workDir), "scan_0001.png");
        TestImageFactory.CreateStandardTwoPhotoScan(scanPath);
        var entry = new WorkspaceScanEntry { RelativePath = Path.GetRelativePath(_workDir, scanPath) };
        _session.Scans.Add(entry);

        using var engine = new PhotoCropperEngine(scanPath) { AutoOrientPhotos = false };
        engine.DetectPhotos();
        var crops = WorkspaceCropData.FromEngine(engine);
        entry.SetFinalCrops(crops);
        var photos = PhotoPipelineService.SaveCroppedPhotos(_workDir, _session, entry,
            [.. engine.DetectedPhotos.Select((p, i) => new CroppedPhotoInput(p, crops[i]))], RestorationSettings.None);
        return (entry, photos);
    }

    [Fact]
    public void RotatePhoto_ShouldTurnTheFileAndRecordTheTurnOnTheScan()
    {
        var (scan, photos) = CreateProject();
        var photo = photos[0];
        string path = photo.Cropped!.GetFullPath(_workDir);
        using var before = Cv2.ImRead(path);

        PhotoPipelineService.RotatePhoto(_workDir, _session, photo, clockwise: true);

        using var after = Cv2.ImRead(path);
        after.Size().ShouldBe(new Size(before.Height, before.Width));
        using var expected = new Mat();
        Cv2.Rotate(before, expected, RotateFlags.Rotate90Clockwise);
        Cv2.Norm(after, expected, NormTypes.L1).ShouldBe(0); // lossless
        scan.FinalCrops[0].RotationDegrees.ShouldBe(90);
        photo.IsFinalStale.ShouldBeTrue();
    }

    [Fact]
    public void RotatePhoto_ThenRestoringTheScan_ShouldGiveTheSamePhoto()
    {
        // What the Crops stage does next time it opens the scan: restore its photos from the saved boxes.
        var (scan, photos) = CreateProject();
        PhotoPipelineService.RotatePhoto(_workDir, _session, photos[1], clockwise: false);

        using var engine = new PhotoCropperEngine(Path.Combine(_workDir, scan.RelativePath)) { AutoOrientPhotos = false };
        engine.RestoreFromSavedCrops(scan.FinalCrops);

        using var cropped = Cv2.ImRead(photos[1].Cropped!.GetFullPath(_workDir), ImreadModes.Unchanged);
        engine.DetectedPhotos[1].Size().ShouldBe(cropped.Size());
        engine.DetectedPhotos[1].Type().ToString().ShouldBe(cropped.Type().ToString());
        Cv2.Norm(engine.DetectedPhotos[1], cropped, NormTypes.L1).ShouldBe(0);
    }

    [Fact]
    public void RotatePhoto_FourTimes_ShouldGiveTheOriginalBack()
    {
        var (_, photos) = CreateProject();
        string path = photos[0].Cropped!.GetFullPath(_workDir);
        byte[] original = File.ReadAllBytes(path);

        for (int i = 0; i < 4; i++) PhotoPipelineService.RotatePhoto(_workDir, _session, photos[0], clockwise: true);

        File.ReadAllBytes(path).ShouldBe(original);
        photos[0].Crop!.RotationDegrees.ShouldBe(0);
    }

    [Fact]
    public void RotatePhoto_ShouldTurnRepairStrokesAndTheHandSetTrim()
    {
        var (_, photos) = CreateProject();
        var photo = photos[0];
        using (var image = Cv2.ImRead(photo.Cropped!.GetFullPath(_workDir)))
        {
            photo.RepairImageWidth = image.Width;
            photo.RepairImageHeight = image.Height;
        }
        var stroke = new RepairStroke { Radius = 0.02f };
        stroke.Points.Add(0.1f);
        stroke.Points.Add(0.2f);
        photo.Repairs.Add(stroke);
        photo.Restoration = photo.Restoration with { TrimWhiteBorders = true, ManualTrim = new TrimArea { Left = 0.1, Top = 0.2, Right = 0.7, Bottom = 0.9 } };
        int width = photo.RepairImageWidth, height = photo.RepairImageHeight;

        PhotoPipelineService.RotatePhoto(_workDir, _session, photo, clockwise: true);

        // Clockwise: a point at (x, y) lands at (1 - y, x).
        photo.Repairs[0].Points[0].ShouldBe(0.8f, 1e-6f);
        photo.Repairs[0].Points[1].ShouldBe(0.1f, 1e-6f);
        photo.Repairs[0].Radius.ShouldBe(0.02f * width / height, 1e-6f); // same size in pixels on the new width
        (photo.RepairImageWidth, photo.RepairImageHeight).ShouldBe((height, width));
        photo.AreRepairsOutdated(height, width).ShouldBeFalse();
        var trim = photo.Restoration.ManualTrim!;
        (trim.Left, trim.Top, trim.Right, trim.Bottom).ShouldBe((1 - 0.9, 0.1, 1 - 0.2, 0.7));
    }
}
