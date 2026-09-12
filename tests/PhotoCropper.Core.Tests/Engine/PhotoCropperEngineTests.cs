using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.TestHelpers;
using System.Drawing;

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
    public void Constructor_ShouldLoadImage()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);
        cropper.Original.IsEmpty.ShouldBeFalse();
        cropper.Original.Width.ShouldBe(2000);
    }

    [Fact]
    public void DetectPhotos_StandardScan_ShouldFindTwoPhotos()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);
        cropper.DetectPhotos();
        cropper.DetectedPhotos.Count.ShouldBeGreaterThanOrEqualTo(2);
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
        cropper.AddManualCrop(new Rectangle(x, y, w, h));

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

        using Mat messy = new(400, 400, DepthType.Cv8U, 3);
        messy.SetTo(new MCvScalar(255, 255, 255));
        CvInvoke.Rectangle(messy, new Rectangle(100, 100, 200, 200), new MCvScalar(0, 0, 0), -1);

        cropper.DetectedPhotos.Add(messy.Clone());
        Rectangle refined = cropper.GetRefinedCropRect(0);

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
            CustomBackgroundColorHsv = new MCvScalar(10, 20, 30),
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
        cropper.CustomBackgroundColorHsv.ShouldBe(new MCvScalar(10, 20, 30));
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
        cropper.RotatePhoto(-1);
        cropper.RotatePhoto(100);
    }

    [Fact]
    public void GetRefinedCropRect_OutOfBounds_ShouldReturnEmpty()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);
        cropper.GetRefinedCropRect(-1).ShouldBe(Rectangle.Empty);
        cropper.GetRefinedCropRect(100).ShouldBe(Rectangle.Empty);
    }

    [Fact]
    public void ApplyCropToPhoto_OutOfBounds_ShouldNotThrow()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);
        cropper.ApplyCropToPhoto(-1, new Rectangle(0, 0, 10, 10));
        cropper.ApplyCropToPhoto(100, new Rectangle(0, 0, 10, 10));
    }

    [Fact]
    public void SaveDetectedPhotos_ShouldExportFiles()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);
        cropper.DetectPhotos();

        string outDir = Path.Combine(_tempDir, "exported_photos");
        cropper.SaveDetectedPhotos(outDir, "PNG");

        Directory.Exists(outDir).ShouldBeTrue();
        var files = Directory.GetFiles(outDir, "*.png");
        files.Length.ShouldBe(cropper.DetectedPhotos.Count);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
