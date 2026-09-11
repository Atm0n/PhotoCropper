using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Detection;
using PhotoCropper.Extraction;
using PhotoCropper.Models;

namespace PhotoCropper.Tests;

public sealed class ModularServiceTests : IDisposable
{
    private readonly string _tempDir;

    public ModularServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ModularTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void BackgroundAnalyzer_ShouldDetectWhiteBackground()
    {
        using Mat whiteBgr = new(500, 500, DepthType.Cv8U, 3);
        whiteBgr.SetTo(new MCvScalar(255, 255, 255));
        using Mat whiteHsv = new();
        CvInvoke.CvtColor(whiteBgr, whiteHsv, ColorConversion.Bgr2Hsv);

        MCvScalar bgHsv = BackgroundAnalyzer.SampleBackgroundColor(whiteHsv);
        Assert.True(bgHsv.V2 >= 250); // High Value (brightness)
    }

    [Fact]
    public void CandidateResolutionFilter_ShouldDiscardCompositeCandidateInFavorOfChildren()
    {
        // Parent candidate spanning 1000x500
        Point[] parentPts = [new Point(0, 0), new Point(1000, 0), new Point(1000, 500), new Point(0, 500)];
        var parent = new CropCandidate(parentPts, new Rectangle(0, 0, 1000, 500), 500000, new RotatedRect(new PointF(500, 250), new SizeF(1000, 500), 0), 500000, 0.9, 0.9);

        // Child 1 spanning (0, 0, 480, 500)
        Point[] child1Pts = [new Point(0, 0), new Point(480, 0), new Point(480, 500), new Point(0, 500)];
        var child1 = new CropCandidate(child1Pts, new Rectangle(0, 0, 480, 500), 240000, new RotatedRect(new PointF(240, 250), new SizeF(480, 500), 0), 240000, 0.95, 0.99);

        // Child 2 spanning (520, 0, 480, 500)
        Point[] child2Pts = [new Point(520, 0), new Point(1000, 0), new Point(1000, 500), new Point(520, 500)];
        var child2 = new CropCandidate(child2Pts, new Rectangle(520, 0, 480, 500), 240000, new RotatedRect(new PointF(760, 250), new SizeF(480, 500), 0), 240000, 0.95, 0.99);

        var filtered = CandidateResolutionFilter.FilterCandidates([parent, child1, child2]);

        // Parent should be rejected as composite, retaining child 1 and child 2
        Assert.Equal(2, filtered.Count);
        Assert.DoesNotContain(filtered, c => c.Area == 500000);
    }

    [Fact]
    public void EdgeRefinementService_ShouldTrimOuterWhiteMargins()
    {
        // 1000x1000 photo with 80px white margin around a dark center
        using Mat photo = new(1000, 1000, DepthType.Cv8U, 3);
        photo.SetTo(new MCvScalar(255, 255, 255)); // White border
        CvInvoke.Rectangle(photo, new Rectangle(80, 80, 840, 840), new MCvScalar(30, 30, 30), -1);

        Rectangle refined = EdgeRefinementService.GetRefinedCropRect(photo);

        Assert.True(refined.X >= 70);
        Assert.True(refined.Y >= 70);
        Assert.True(refined.Width <= 860);
        Assert.True(refined.Height <= 860);
    }

    [Fact]
    public void UndoRedoHistory_ShouldCorrectlyUndoAndRedoOperations()
    {
        string dummyScan = Path.Combine(_tempDir, "undo_test_scan.jpg");
        using (Mat scan = new(500, 500, DepthType.Cv8U, 3))
        {
            scan.SetTo(new MCvScalar(255, 255, 255));
            scan.Save(dummyScan);
        }

        using var engine1 = new PhotoCropperEngine(dummyScan);
        using var engine2 = new PhotoCropperEngine(dummyScan);
        var engines = new List<PhotoCropperEngine> { engine1, engine2 };
        using var history = new PhotoCropperGui.Services.UndoRedoHistory();

        using Mat photo1 = new(200, 100, DepthType.Cv8U, 3);
        photo1.SetTo(new MCvScalar(10, 10, 10));
        // Mat(rows: 200, cols: 100) -> Width is 100, Height is 200
        engine1.DetectedPhotos.Add(photo1.Clone());

        using Mat photo2 = new(300, 300, DepthType.Cv8U, 3);
        photo2.SetTo(new MCvScalar(20, 20, 20));
        engine2.DetectedPhotos.Add(photo2.Clone());

        // 1. Delete on scan 0
        var p1 = engine1.DetectedPhotos[0];
        history.PushDelete(0, 0, p1);
        engine1.DeletePhoto(0);
        Assert.Empty(engine1.DetectedPhotos);

        // 2. Delete on scan 1
        var p2 = engine2.DetectedPhotos[0];
        history.PushDelete(1, 0, p2);
        engine2.DeletePhoto(0);
        Assert.Empty(engine2.DetectedPhotos);

        // 3. Undo #1 (should restore photo to scan 1)
        var action1 = history.Undo(engines);
        Assert.NotNull(action1);
        Assert.Equal(1, action1.ScanIndex);
        Assert.Single(engine2.DetectedPhotos);
        Assert.Empty(engine1.DetectedPhotos);

        // 4. Undo #2 (should restore photo to scan 0)
        var action2 = history.Undo(engines);
        Assert.NotNull(action2);
        Assert.Equal(0, action2.ScanIndex);
        Assert.Single(engine1.DetectedPhotos);
        Assert.Single(engine2.DetectedPhotos);
    }

    [Fact]
    public void PhotoExporter_ShouldPreserveDpiAndReportProgress()
    {
        string scanPath = Path.Combine(_tempDir, "dpi_test_scan.jpg");
        using (Mat scan = new(300, 300, DepthType.Cv8U, 3))
        {
            scan.SetTo(new MCvScalar(200, 200, 200));
            scan.Save(scanPath);
        }

        // Set source JPEG DPI to 600x600
        PhotoCropper.Export.PhotoExporter.EmbedJpegDpi(scanPath, 600, 600);
        var sourceDpi = PhotoCropper.Export.PhotoExporter.GetDpiFromSource(scanPath);
        Assert.Equal(600, sourceDpi.XDpi);
        Assert.Equal(600, sourceDpi.YDpi);

        using Mat photo1 = new(100, 100, DepthType.Cv8U, 3);
        photo1.SetTo(new MCvScalar(50, 50, 50));

        int progressUpdates = 0;
        string exportDir = Path.Combine(_tempDir, "dpi_export");

        PhotoCropper.Export.PhotoExporter.SavePhotos(
            [photo1], 
            scanPath, 
            exportDir, 
            "JPEG", 
            90, 
            (done, total) => { progressUpdates++; });

        Assert.Equal(1, progressUpdates);
        string[] exportedFiles = Directory.GetFiles(exportDir, "*.jpg");
        Assert.Single(exportedFiles);

        var exportedDpi = PhotoCropper.Export.PhotoExporter.GetDpiFromSource(exportedFiles[0]);
        Assert.Equal(600, exportedDpi.XDpi);
        Assert.Equal(600, exportedDpi.YDpi);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
