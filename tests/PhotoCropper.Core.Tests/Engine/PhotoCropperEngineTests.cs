using OpenCvSharp;
using PhotoCropper.Core.Common;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Core.Tests.Engine;

public sealed class PhotoCropperEngineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _standardScanPath;

    public PhotoCropperEngineTests()
    {
        _tempDir = TestImageFactory.CreateTempDirectory("EngineTests");
        _standardScanPath = Path.Combine(_tempDir, "standard_scan.jpg");
        TestImageFactory.CreateStandardTwoPhotoScan(_standardScanPath);
    }

    [Fact]
    public void RestoreFromSavedCrops_ShouldBypassDetectionAndLoadCrops()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);

        var savedCrops = new List<PhotoCropper.Core.Workspace.WorkspaceCropData>
        {
            new() { CenterX = 100, CenterY = 100, Width = 50, Height = 50, Angle = 0 }
        };

        cropper.RestoreFromSavedCrops(savedCrops);

        // Normally DetectPhotos would find 2 photos in the standard scan. 
        // Here we bypassed it and injected only 1 crop.
        cropper.DetectedPhotos.Count.ShouldBe(1);
        cropper.AcceptedCandidates.Count.ShouldBe(1);
        cropper.AcceptedCandidates[0].Rotated.Size.Width.ShouldBe(50);
    }

    [Fact]
    public void AutoTune_AfterDetecting_GivesTheSameBoxesAsDetectingWithItsSettings()
    {
        using var tuned = new PhotoCropperEngine(_standardScanPath);
        tuned.DetectPhotos();
        tuned.AutoTune();

        using var fresh = new PhotoCropperEngine(_standardScanPath);
        fresh.ApplyOptions(tuned.CurrentOptions);
        fresh.DetectPhotos();

        tuned.AcceptedCandidates.Select(c => c.Rotated).ShouldBe(fresh.AcceptedCandidates.Select(c => c.Rotated));
        tuned.DetectedPhotos.Count.ShouldBe(fresh.DetectedPhotos.Count);
    }

    [Fact]
    public void AutoTune_AfterABoxWasDeleted_DetectsAgain()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);
        cropper.DetectPhotos();
        int found = cropper.AcceptedCandidates.Count;
        found.ShouldBeGreaterThan(1);

        cropper.DeletePhoto(0);
        cropper.AutoTune();

        cropper.AcceptedCandidates.Count.ShouldBe(found);
        cropper.DetectedPhotos.Count.ShouldBe(found);
    }

    [Fact]
    public void Constructor_ShouldLoadImage()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);
        cropper.Original.Empty().ShouldBeFalse();
        cropper.Original.Width.ShouldBe(2000);
    }

    [Fact]
    public void DetectPhotos_StandardScan_ShouldFindTwoPhotos()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);
        cropper.DetectPhotos();
        cropper.DetectedPhotos.Count.ShouldBeGreaterThanOrEqualTo(2);
        cropper.TotalDetectedAreaRatio.ShouldBeGreaterThan(0.0);
        cropper.TotalDetectedAreaRatio.ShouldBeLessThanOrEqualTo(1.0);
    }

    [Fact]
    public void RotatePhoto_ShouldSwapDimensions()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);
        cropper.DetectPhotos();

        var photo = cropper.DetectedPhotos[0];
        int w = photo.Width;
        int h = photo.Height;

        cropper.RotatePhoto(0);

        cropper.DetectedPhotos[0].Width.ShouldBe(h);
        cropper.DetectedPhotos[0].Height.ShouldBe(w);
    }

    [Fact]
    public void RotatePhoto_FourTimes_ShouldRestoreDimensions()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);
        cropper.DetectPhotos();

        int w = cropper.DetectedPhotos[0].Width;
        int h = cropper.DetectedPhotos[0].Height;

        for (int i = 0; i < 4; i++) cropper.RotatePhoto(0);

        cropper.DetectedPhotos[0].Width.ShouldBe(w);
        cropper.DetectedPhotos[0].Height.ShouldBe(h);
    }

    [Fact]
    public void DeletePhoto_ShouldDecreaseCount()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);
        cropper.DetectPhotos();
        int initialCount = cropper.DetectedPhotos.Count;

        cropper.DeletePhoto(0);

        cropper.DetectedPhotos.Count.ShouldBe(initialCount - 1);
    }

    [Fact]
    public void DeletePhoto_InvalidIndex_ShouldNotCrash()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);
        cropper.DetectPhotos();
        int count = cropper.DetectedPhotos.Count;

        cropper.DeletePhoto(-1);
        cropper.DeletePhoto(999);

        cropper.DetectedPhotos.Count.ShouldBe(count);
    }

    [Theory]
    [InlineData(1500, 1500, 100, 100, true)]  // Valid crop
    [InlineData(1950, 1950, 200, 200, true)]  // Clamped out of bounds
    [InlineData(10, 10, 2, 2, false)]         // Too small
    [InlineData(80, 80, 450, 450, true)]      // Snaps to Photo 1
    public void AddManualCrop_VariousCases(int x, int y, int w, int h, bool expectedAdded)
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);
        cropper.AddManualCrop(new System.Drawing.Rectangle(x, y, w, h));

        if (expectedAdded)
        {
            cropper.DetectedPhotos.ShouldHaveSingleItem();
        }
        else
        {
            cropper.DetectedPhotos.ShouldBeEmpty();
        }
    }

    [Fact]
    public void GetRefinedCropRect_ShouldRemoveMargins()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);

        using Mat messy = new(400, 400, MatType.CV_8UC3);
        messy.SetTo(new Scalar(255, 255, 255));
        Cv2.Rectangle(messy, new Rect(100, 100, 200, 200), new Scalar(0, 0, 0), -1);

        cropper.DetectedPhotos.Add(messy.Clone());
        System.Drawing.Rectangle refined = cropper.GetRefinedCropRect(0);

        refined.Width.ShouldBeInRange(190, 205);
        refined.Height.ShouldBeInRange(190, 205);

        cropper.ApplyCropToPhoto(0, refined);
        cropper.DetectedPhotos[0].Width.ShouldBe(refined.Width);
    }

    [Fact]
    public void DetectPhotos_WithPhotoInCorner_ShouldDetectSuccessfully()
    {
        string path = Path.Combine(_tempDir, "corner_scan.jpg");
        TestImageFactory.CreateCornerPhotoScan(path);

        using var cropper = new PhotoCropperEngine(path);
        cropper.DetectPhotos();

        cropper.DetectedPhotos.Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void DetectPhotos_HighResolution_ShouldDetectSuccessfully()
    {
        string path = Path.Combine(_tempDir, "highres_scan.jpg");
        TestImageFactory.CreateHighResScan(path);

        using var cropper = new PhotoCropperEngine(path);
        cropper.DetectPhotos();

        cropper.DetectedPhotos.ShouldNotBeEmpty();
    }

    [Fact]
    public void DetectPhotos_TiltedAdjacent_ShouldDetectWithoutOverlapConflict()
    {
        string path = Path.Combine(_tempDir, "tilted_adjacent.jpg");
        TestImageFactory.CreateTiltedAdjacentScan(path);

        using var cropper = new PhotoCropperEngine(path);
        cropper.DetectPhotos();

        cropper.DetectedPhotos.Count.ShouldBe(2);
    }

    [Fact]
    public void DetectPhotos_DarkBackground_ShouldDetectSuccessfully()
    {
        string path = Path.Combine(_tempDir, "dark_scan.jpg");
        TestImageFactory.CreateDarkBackgroundScan(path);

        using var cropper = new PhotoCropperEngine(path);
        cropper.DetectPhotos();

        cropper.DetectedPhotos.Count.ShouldBe(1);
        cropper.DetectedPhotos[0].Width.ShouldBeInRange(580, 620);
        cropper.DetectedPhotos[0].Height.ShouldBeInRange(380, 420);
    }

    [Theory]
    [InlineData(4, 0.05)] // a tight tolerance and large minimum, as a user may have set
    [InlineData(30, 0.01)] // the defaults
    public void DetectPhotos_DarkAlbumPageOnLightBed_ShouldFindEveryPhoto(double tolerance, double minArea)
    {
        string path = Path.Combine(_tempDir, "album_page.png");
        TestImageFactory.CreateAlbumPageOnBedScan(path);

        using var cropper = new PhotoCropperEngine(path);
        cropper.ApplyOptions(new PhotoCropper.Core.Models.DetectionOptions { BackgroundTolerance = tolerance, MinAreaFactor = minArea });
        cropper.DetectPhotos();

        cropper.AcceptedCandidates.Count.ShouldBe(TestImageFactory.AlbumPagePhotos.Length);
        foreach (var photo in TestImageFactory.AlbumPagePhotos)
        {
            // Each photo is found whole, its light border included, and not merged with the page.
            cropper.AcceptedCandidates.ShouldContain(c =>
                Math.Abs(c.Rotated.Center.X - (photo.X + photo.Width / 2f)) < 15 && Math.Abs(c.Rotated.Center.Y - (photo.Y + photo.Height / 2f)) < 15 &&
                Math.Abs(Math.Max(c.Rotated.Size.Width, c.Rotated.Size.Height) - Math.Max(photo.Width, photo.Height)) < 30);
        }
    }

    [Theory]
    [InlineData(4, 0.05)]
    [InlineData(30, 0.01)]
    public void DetectPhotos_PrintReachingTheEdgeWithALightPart_ShouldKeepItsWholeBox(double tolerance, double minArea)
    {
        // Prints on the bed, not a page: the white floor where one runs off the scan's edge isn't bed to paint out.
        string path = Path.Combine(_tempDir, "prints_to_edge.png");
        TestImageFactory.CreatePrintsReachingTheEdgeScan(path);

        using var cropper = new PhotoCropperEngine(path);
        cropper.ApplyOptions(new PhotoCropper.Core.Models.DetectionOptions { BackgroundTolerance = tolerance, MinAreaFactor = minArea });
        cropper.DetectPhotos();

        cropper.AcceptedCandidates.Count.ShouldBe(TestImageFactory.PrintsReachingTheEdge.Length);
        foreach (var print in TestImageFactory.PrintsReachingTheEdge)
        {
            cropper.AcceptedCandidates.ShouldContain(c =>
                Math.Abs(c.Rotated.Center.X - (print.X + print.Width / 2f)) < 25 && Math.Abs(c.Rotated.Center.Y - (print.Y + print.Height / 2f)) < 25 &&
                Math.Abs(Math.Max(c.Rotated.Size.Width, c.Rotated.Size.Height) - Math.Max(print.Width, print.Height)) < 40 &&
                Math.Abs(Math.Min(c.Rotated.Size.Width, c.Rotated.Size.Height) - Math.Min(print.Width, print.Height)) < 40);
        }
    }

    [Fact]
    public void DetectPhotos_FlushScanEdge_ShouldDetectSuccessfully()
    {
        string path = Path.Combine(_tempDir, "flush_scan.jpg");
        TestImageFactory.CreateFlushEdgeScan(path);

        using var cropper = new PhotoCropperEngine(path);
        cropper.DetectPhotos();

        cropper.DetectedPhotos.Count.ShouldBe(1);
        cropper.DetectedPhotos[0].Width.ShouldBeInRange(480, 520);
        cropper.DetectedPhotos[0].Height.ShouldBeInRange(380, 420);
    }

    [Fact]
    public void DetectPhotos_CloselySpaced_ShouldSeparatePhotos()
    {
        string path = Path.Combine(_tempDir, "close_photos.jpg");
        TestImageFactory.CreateCloselySpacedScan(path, gap: 20);

        using var cropper = new PhotoCropperEngine(path);
        cropper.DetectPhotos();

        cropper.DetectedPhotos.Count.ShouldBe(2);
    }

    [Fact]
    public void DetectPhotos_UltraClosePhotos_ShouldNotMergeIntoOneBigImage()
    {
        string path = Path.Combine(_tempDir, "ultra_close.jpg");
        TestImageFactory.CreateCloselySpacedScan(path, gap: 12);

        using var cropper = new PhotoCropperEngine(path);
        cropper.DetectPhotos();

        cropper.DetectedPhotos.Count.ShouldBe(2);
        foreach (var photo in cropper.DetectedPhotos)
        {
            photo.Width.ShouldBeInRange(470, 530);
            photo.Height.ShouldBeInRange(470, 530);
        }
    }

    [Fact]
    public void SetCustomBackgroundFromPixel_ShouldSampleCorrectlyAndOverrideBackground()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);

        // Sample at 0, 0 (white background)
        cropper.SetCustomBackgroundFromPixel(0, 0);
        cropper.CustomBackgroundColorHsv.ShouldNotBeNull();
        var whiteHsv = cropper.CustomBackgroundColorHsv.Value;
        whiteHsv.V0.ShouldBeInRange(0, 10);
        whiteHsv.V1.ShouldBeInRange(0, 10);
        whiteHsv.V2.ShouldBeInRange(245, 256);

        // Sample at 200, 200 (black photo)
        cropper.SetCustomBackgroundFromPixel(200, 200);
        cropper.CustomBackgroundColorHsv.ShouldNotBeNull();
        var blackHsv = cropper.CustomBackgroundColorHsv.Value;
        blackHsv.V2.ShouldBeInRange(0, 10);

        // Reset
        cropper.CustomBackgroundColorHsv = null;
        cropper.CustomBackgroundColorHsv.ShouldBeNull();
    }

    [Fact]
    public void ApplyOptions_AndCurrentOptions_ShouldRoundtrip()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);
        var options = new Models.DetectionOptions
        {
            BackgroundTolerance = 45,
            MinAreaFactor = 0.05,
            MaxAreaFactor = 0.80,
            CannyLowThreshold = 30,
            CannyHighThreshold = 70,
            CustomBackgroundColorHsv = new Models.ColorScalar(10, 20, 30),
            AutoOrientPhotos = false,
            RestoreVintageColors = false,
            RemoveDustAndScratches = false
        };

        cropper.ApplyOptions(options);

        cropper.BackgroundTolerance.ShouldBe(45);
        cropper.MinAreaFactor.ShouldBe(0.05);
        cropper.MaxAreaFactor.ShouldBe(0.80);
        cropper.CannyLowThreshold.ShouldBe(30);
        cropper.CannyHighThreshold.ShouldBe(70);
        cropper.CustomBackgroundColorHsv.ShouldBe(new Models.ColorScalar(10, 20, 30));
        cropper.AutoOrientPhotos.ShouldBeFalse();
        cropper.RestoreVintageColors.ShouldBeFalse();
        cropper.RemoveDustAndScratches.ShouldBeFalse();

        var retrieved = cropper.CurrentOptions;
        retrieved.BackgroundTolerance.ShouldBe(45);
        retrieved.MinAreaFactor.ShouldBe(0.05);
        retrieved.AutoOrientPhotos.ShouldBeFalse();
    }

    [Fact]
    public void ApplyOptions_Null_ShouldThrowArgumentNullException()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);
        Should.Throw<ArgumentNullException>(() => cropper.ApplyOptions(null!));
    }

    [Fact]
    public void RotatePhoto_OutOfBounds_ShouldNotThrow()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);
        Should.NotThrow(() => cropper.RotatePhoto(-1));
        Should.NotThrow(() => cropper.RotatePhoto(100));
    }

    [Fact]
    public void GetRefinedCropRect_OutOfBounds_ShouldReturnEmpty()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);
        cropper.GetRefinedCropRect(-1).ShouldBe(System.Drawing.Rectangle.Empty);
        cropper.GetRefinedCropRect(100).ShouldBe(System.Drawing.Rectangle.Empty);
    }

    [Fact]
    public void ApplyCropToPhoto_OutOfBounds_ShouldNotThrow()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);
        Should.NotThrow(() => cropper.ApplyCropToPhoto(-1, new System.Drawing.Rectangle(0, 0, 10, 10)));
        Should.NotThrow(() => cropper.ApplyCropToPhoto(100, new System.Drawing.Rectangle(0, 0, 10, 10)));

    }

    [Fact]
    public void DetectPhotos_ScannerWithBezel_ShouldDetectAllPhotos()
    {
        string bezelScanPath = Path.Combine(_tempDir, "bezel_scan.jpg");
        TestImageFactory.CreateScannerBezelScan(bezelScanPath, bezelThickness: 15);

        using var cropper = new PhotoCropperEngine(bezelScanPath);
        cropper.DetectPhotos();

        cropper.DetectedPhotos.Count.ShouldBe(2);
    }

    [Fact]
    public void DetectPhotos_SlightlyTiltedPhoto_ShouldDetectInclinationAndStraighten()
    {
        string tiltedScanPath = Path.Combine(_tempDir, "tilted_scan.jpg");
        using (Mat scan = new(2000, 2000, MatType.CV_8UC3))
        {
            scan.SetTo(new Scalar(255, 255, 255));
            // Photo tilted by ~3 degrees: center at (600, 600), size (400, 500), angle = 3.0
            RotatedRect targetRect = new(new Point2f(600, 600), new Size2f(400, 500), 3.0f);
            Point2f[] vertices = targetRect.Points();
            Point[] polyPoints = [.. vertices.Select(v => new Point((int)Math.Round(v.X), (int)Math.Round(v.Y)))];
            Point[] vp = polyPoints;
            Cv2.FillConvexPoly(scan, vp, new Scalar(20, 20, 20));
            Cv2.ImWrite(tiltedScanPath, scan);
        }

        using var cropper = new PhotoCropperEngine(tiltedScanPath);
        cropper.DetectPhotos();

        cropper.DetectedPhotos.Count.ShouldBe(1);
        var detected = cropper.DetectedPhotos[0];
        int minDim = Math.Min(detected.Width, detected.Height);
        int maxDim = Math.Max(detected.Width, detected.Height);
        minDim.ShouldBeInRange(380, 420);
        maxDim.ShouldBeInRange(480, 520);
    }

    [Fact]
    public void NormalizeToBgr_DifferentChannels_ShouldAlwaysReturn3Channels()
    {
        using var grayMat = new Mat(50, 50, MatType.CV_8UC1);
        grayMat.SetTo(new Scalar(100));
        using var bgrFromGray = ImageIo.NormalizeToBgr(grayMat);
        bgrFromGray.Channels().ShouldBe(3);
        bgrFromGray.Width.ShouldBe(50);
        bgrFromGray.Height.ShouldBe(50);

        using var bgrMat = new Mat(50, 50, MatType.CV_8UC3);
        bgrMat.SetTo(new Scalar(10, 20, 30));
        using var bgrFromBgr = ImageIo.NormalizeToBgr(bgrMat);
        bgrFromBgr.Channels().ShouldBe(3);

        using var bgraMat = new Mat(50, 50, MatType.CV_8UC4);
        bgraMat.SetTo(new Scalar(10, 20, 30, 255));
        using var bgrFromBgra = ImageIo.NormalizeToBgr(bgraMat);
        bgrFromBgra.Channels().ShouldBe(3);
    }

    [Fact]
    public void DetectPhotos_GrayscaleScan_ShouldDetectPhotosWithoutThrowing()
    {
        string grayScanPath = Path.Combine(_tempDir, "gray_scan.png");
        using (Mat scan = new(1000, 1000, MatType.CV_8UC1))
        {
            scan.SetTo(new Scalar(255));
            Cv2.Rectangle(scan, new Rect(100, 100, 300, 300), new Scalar(0), -1);
            Cv2.ImWrite(grayScanPath, scan);
        }

        using var cropper = new PhotoCropperEngine(grayScanPath);
        cropper.Original.Channels().ShouldBe(3);
        cropper.DetectPhotos();
        cropper.DetectedPhotos.Count.ShouldBe(1);
    }

    [Fact]
    public void ApplyGrabHandleResize_ShouldExtractPhotoAndReplaceInList()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);
        cropper.DetectPhotos();

        int initialPhotosCount = cropper.DetectedPhotos.Count;
        int candIndex = 0;

        // Modify the candidate's RotatedRect directly to simulate a user resize
        var cand = cropper.AcceptedCandidates[candIndex];
        var newRect = new Models.RotatedBox(cand.Rotated.Center, new System.Drawing.SizeF(50, 50), cand.Rotated.Angle);
        var list = cropper.AcceptedCandidates.ToList();
        list[candIndex] = cand with { Rotated = newRect };
        cropper.UpdateCandidates(list);

        // Apply the resize
        cropper.ApplyGrabHandleResize(candIndex);

        cropper.DetectedPhotos.Count.ShouldBe(initialPhotosCount);

        var newlyExtracted = cropper.DetectedPhotos[candIndex];
        // After extraction, the width and height should be roughly the RotatedRect's size.
        // It might be slightly off depending on rotation interpolation, but for 0-angle it's exact.
        newlyExtracted.Width.ShouldBeInRange(45, 55);
        newlyExtracted.Height.ShouldBeInRange(45, 55);
    }

    [Fact]
    public void RestoreFromSavedCrops_EmptyList_ShouldNotCrash()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);

        cropper.RestoreFromSavedCrops([]);

        cropper.DetectedPhotos.ShouldBeEmpty();
        cropper.AcceptedCandidates.ShouldBeEmpty();
    }

    [Fact]
    public void SetCustomBackgroundFromPixel_OutsideBounds_ShouldNotThrow()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);

        // Sampling outside the image bounds should gracefully ignore or clamp
        Should.NotThrow(() => cropper.SetCustomBackgroundFromPixel(-1, -1));
        Should.NotThrow(() => cropper.SetCustomBackgroundFromPixel(9999, 9999));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
