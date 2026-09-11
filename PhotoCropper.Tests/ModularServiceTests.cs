using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using PhotoCropper.Detection;
using PhotoCropper.Extraction;
using PhotoCropper.Models;
using System.Drawing;

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

    [Fact]
    public void PhotoExtractionEngine_RegularizeNearRightAngles_ShouldSnapNearStraightAnglesPreservingDimensions()
    {
        // 1. Horizontal rectangle near 0 deg: SizeF(600, 400), Angle=0.8
        RotatedRect hRect = new(new PointF(500, 500), new SizeF(600, 400), 0.8f);
        RotatedRect hSnapped = PhotoExtractionEngine.RegularizeNearRightAngles(hRect);
        Assert.Equal(0f, hSnapped.Angle);
        Assert.True(Math.Abs(hSnapped.Size.Width - 600) < 5);
        Assert.True(Math.Abs(hSnapped.Size.Height - 400) < 5);

        // 2. Vertical rectangle near 0 deg: SizeF(400, 600), Angle=-0.9
        RotatedRect vRect = new(new PointF(500, 500), new SizeF(400, 600), -0.9f);
        RotatedRect vSnapped = PhotoExtractionEngine.RegularizeNearRightAngles(vRect);
        Assert.Equal(0f, vSnapped.Angle);
        Assert.True(Math.Abs(vSnapped.Size.Width - 400) < 5);
        Assert.True(Math.Abs(vSnapped.Size.Height - 600) < 5);

        // 3. Vertical rectangle near 90 deg: SizeF(600, 400), Angle=89.2
        RotatedRect vRect90 = new(new PointF(500, 500), new SizeF(600, 400), 89.2f);
        RotatedRect vSnapped90 = PhotoExtractionEngine.RegularizeNearRightAngles(vRect90);
        Assert.Equal(0f, vSnapped90.Angle);
        // At Angle=0, the horizontal dimension should be ~400 and vertical should be ~600
        Assert.True(Math.Abs(vSnapped90.Size.Width - 400) < 5);
        Assert.True(Math.Abs(vSnapped90.Size.Height - 600) < 5);

        // 4. Truly tilted rectangle (25 deg): unchanged
        RotatedRect tilted = new(new PointF(500, 500), new SizeF(600, 400), 25.0f);
        RotatedRect tiltedRes = PhotoExtractionEngine.RegularizeNearRightAngles(tilted);
        Assert.Equal(25.0f, tiltedRes.Angle);
    }

    [Fact]
    public void PhotoExtractionEngine_ExtractPhotoFromContour_ShouldPreserveNaturalOrientation()
    {
        // 1. Vertical photo on scan: 300 wide, 500 high
        using Mat scanMat = new(800, 800, DepthType.Cv8U, 3);
        scanMat.SetTo(new MCvScalar(255, 255, 255));
        CvInvoke.Rectangle(scanMat, new Rectangle(100, 100, 300, 500), new MCvScalar(50, 50, 50), -1);

        Point[] vPoints = [
            new Point(100, 100),
            new Point(400, 100),
            new Point(400, 600),
            new Point(100, 600)
        ];
        using VectorOfPoint vPoly = new(vPoints);
        using Mat vExtracted = PhotoExtractionEngine.ExtractPhotoFromContour(vPoly, scanMat);

        // Vertical photo should stay vertical (height > width)
        Assert.False(vExtracted.IsEmpty);
        Assert.True(vExtracted.Height > vExtracted.Width);
        Assert.True(Math.Abs(vExtracted.Width - 300) <= 2);
        Assert.True(Math.Abs(vExtracted.Height - 500) <= 2);

        // 2. Horizontal photo on scan: 500 wide, 300 high
        CvInvoke.Rectangle(scanMat, new Rectangle(200, 200, 500, 300), new MCvScalar(80, 80, 80), -1);
        Point[] hPoints = [
            new Point(200, 200),
            new Point(700, 200),
            new Point(700, 500),
            new Point(200, 500)
        ];
        using VectorOfPoint hPoly = new(hPoints);
        using Mat hExtracted = PhotoExtractionEngine.ExtractPhotoFromContour(hPoly, scanMat);

        // Horizontal photo should stay horizontal (width > height)
        Assert.False(hExtracted.IsEmpty);
        Assert.True(hExtracted.Width > hExtracted.Height);
        Assert.True(Math.Abs(hExtracted.Width - 500) <= 2);
        Assert.True(Math.Abs(hExtracted.Height - 300) <= 2);
    }

    [Fact]
    public void AutoOrientationService_ShouldDetectSkyOrientationCorrectly()
    {
        // 1. Upright photo: Sky (BGR: 235, 180, 70) at top (y: 0-100), Dark ground (BGR: 20, 40, 20) at bottom (y: 100-200)
        using Mat upright = new(200, 200, DepthType.Cv8U, 3);
        CvInvoke.Rectangle(upright, new Rectangle(0, 0, 200, 100), new MCvScalar(235, 180, 70), -1); // BGR Blue Sky
        CvInvoke.Rectangle(upright, new Rectangle(0, 100, 200, 100), new MCvScalar(20, 40, 20), -1); // Ground
        Assert.Equal(0, AutoOrientationService.DetectRequiredRotation(upright));

        // 2. Upside-down photo: Sky at bottom (y: 100-200), Ground at top (y: 0-100)
        using Mat upsideDown = new(200, 200, DepthType.Cv8U, 3);
        CvInvoke.Rectangle(upsideDown, new Rectangle(0, 0, 200, 100), new MCvScalar(20, 40, 20), -1);
        CvInvoke.Rectangle(upsideDown, new Rectangle(0, 100, 200, 100), new MCvScalar(235, 180, 70), -1);
        Assert.Equal(180, AutoOrientationService.DetectRequiredRotation(upsideDown));

        // Test OrientPhoto rotates 180
        using Mat oriented = AutoOrientationService.OrientPhoto(upsideDown);
        Assert.Equal(0, AutoOrientationService.DetectRequiredRotation(oriented));

        // 3. Sideways photo: Sky on the left (x: 0-100) -> needs 90 CW rotation
        using Mat sidewaysLeft = new(200, 200, DepthType.Cv8U, 3);
        CvInvoke.Rectangle(sidewaysLeft, new Rectangle(0, 0, 100, 200), new MCvScalar(235, 180, 70), -1);
        CvInvoke.Rectangle(sidewaysLeft, new Rectangle(100, 0, 100, 200), new MCvScalar(20, 40, 20), -1);
        Assert.Equal(90, AutoOrientationService.DetectRequiredRotation(sidewaysLeft));
    }

    [Fact]
    public void FaceOrientationService_ModelShouldBeAvailableAndEmbedded()
    {
        Assert.True(FaceOrientationService.IsModelAvailable);
    }

    [Fact]
    public void FaceOrientationService_NonFaceImage_ShouldReturnNegativeOne()
    {
        using Mat landscape = new(200, 200, DepthType.Cv8U, 3);
        landscape.SetTo(new MCvScalar(200, 100, 50));
        Assert.Equal(-1, FaceOrientationService.DetectFaceRotation(landscape));
    }

    [Fact]
    public void PhotoCropperCli_ShouldProcessDirectoryAndExtractPhotos()
    {
        string inputDir = Path.Combine(_tempDir, "cli_input");
        string outputDir = Path.Combine(_tempDir, "cli_output");
        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(outputDir);

        // Scan 1: 1000x1000 with 2 photos
        string scan1Path = Path.Combine(inputDir, "scan_001.jpg");
        using (Mat scan1 = new(1000, 1000, DepthType.Cv8U, 3))
        {
            scan1.SetTo(new MCvScalar(255, 255, 255));
            CvInvoke.Rectangle(scan1, new Rectangle(50, 50, 400, 400), new MCvScalar(30, 40, 50), -1);
            CvInvoke.Rectangle(scan1, new Rectangle(550, 50, 400, 400), new MCvScalar(70, 80, 90), -1);
            scan1.Save(scan1Path);
        }

        // Scan 2: 1000x1000 with 2 photos
        string scan2Path = Path.Combine(inputDir, "scan_002.jpg");
        using (Mat scan2 = new(1000, 1000, DepthType.Cv8U, 3))
        {
            scan2.SetTo(new MCvScalar(255, 255, 255));
            CvInvoke.Rectangle(scan2, new Rectangle(50, 500, 400, 400), new MCvScalar(20, 20, 20), -1);
            CvInvoke.Rectangle(scan2, new Rectangle(550, 500, 400, 400), new MCvScalar(60, 60, 60), -1);
            scan2.Save(scan2Path);
        }

        int exitCode = PhotoCropperCli.Program.Main(["-i", inputDir, "-o", outputDir, "-f", "PNG", "-v"]);
        Assert.Equal(0, exitCode);

        string[] exportedPngs = Directory.GetFiles(outputDir, "*.png");
        Assert.Equal(4, exportedPngs.Length);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
