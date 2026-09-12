using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Tests.Helpers;
using System.Drawing;

namespace PhotoCropper.Tests.Engine;

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
        Assert.False(cropper.Original.IsEmpty);
        Assert.Equal(2000, cropper.Original.Width);
    }

    [Fact]
    public void DetectPhotos_StandardScan_ShouldFindTwoPhotos()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);
        cropper.DetectPhotos();
        Assert.True(cropper.DetectedPhotos.Count >= 2);
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

        Assert.Equal(h, cropper.DetectedPhotos[0].Width);
        Assert.Equal(w, cropper.DetectedPhotos[0].Height);
    }

    [Fact]
    public void RotatePhoto_FourTimes_ShouldRestoreDimensions()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);
        cropper.DetectPhotos();

        int w = cropper.DetectedPhotos[0].Width;
        int h = cropper.DetectedPhotos[0].Height;

        for (int i = 0; i < 4; i++) cropper.RotatePhoto(0);

        Assert.Equal(w, cropper.DetectedPhotos[0].Width);
        Assert.Equal(h, cropper.DetectedPhotos[0].Height);
    }

    [Fact]
    public void DeletePhoto_ShouldDecreaseCount()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);
        cropper.DetectPhotos();
        int initialCount = cropper.DetectedPhotos.Count;

        cropper.DeletePhoto(0);

        Assert.Equal(initialCount - 1, cropper.DetectedPhotos.Count);
    }

    [Fact]
    public void DeletePhoto_InvalidIndex_ShouldNotCrash()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);
        cropper.DetectPhotos();
        int count = cropper.DetectedPhotos.Count;

        cropper.DeletePhoto(-1);
        cropper.DeletePhoto(999);

        Assert.Equal(count, cropper.DetectedPhotos.Count);
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
            Assert.Single(cropper.DetectedPhotos);
        }
        else
        {
            Assert.Empty(cropper.DetectedPhotos);
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

        Assert.InRange(refined.Width, 190, 205);
        Assert.InRange(refined.Height, 190, 205);

        cropper.ApplyCropToPhoto(0, refined);
        Assert.Equal(refined.Width, cropper.DetectedPhotos[0].Width);
    }

    [Fact]
    public void DetectPhotos_WithPhotoInCorner_ShouldDetectSuccessfully()
    {
        string path = Path.Combine(_tempDir, "corner_scan.jpg");
        TestImageFactory.CreateCornerPhotoScan(path);

        using var cropper = new PhotoCropperEngine(path);
        cropper.DetectPhotos();

        Assert.True(cropper.DetectedPhotos.Count >= 2);
    }

    [Fact]
    public void DetectPhotos_HighResolution_ShouldDetectSuccessfully()
    {
        string path = Path.Combine(_tempDir, "highres_scan.jpg");
        TestImageFactory.CreateHighResScan(path);

        using var cropper = new PhotoCropperEngine(path);
        cropper.DetectPhotos();

        Assert.NotEmpty(cropper.DetectedPhotos);
    }

    [Fact]
    public void DetectPhotos_TiltedAdjacent_ShouldDetectWithoutOverlapConflict()
    {
        string path = Path.Combine(_tempDir, "tilted_adjacent.jpg");
        TestImageFactory.CreateTiltedAdjacentScan(path);

        using var cropper = new PhotoCropperEngine(path);
        cropper.DetectPhotos();

        Assert.Equal(2, cropper.DetectedPhotos.Count);
    }

    [Fact]
    public void DetectPhotos_DarkBackground_ShouldDetectSuccessfully()
    {
        string path = Path.Combine(_tempDir, "dark_scan.jpg");
        TestImageFactory.CreateDarkBackgroundScan(path);

        using var cropper = new PhotoCropperEngine(path);
        cropper.DetectPhotos();

        Assert.Single(cropper.DetectedPhotos);
        Assert.InRange(cropper.DetectedPhotos[0].Width, 580, 620);
        Assert.InRange(cropper.DetectedPhotos[0].Height, 380, 420);
    }

    [Fact]
    public void DetectPhotos_FlushScanEdge_ShouldDetectSuccessfully()
    {
        string path = Path.Combine(_tempDir, "flush_scan.jpg");
        TestImageFactory.CreateFlushEdgeScan(path);

        using var cropper = new PhotoCropperEngine(path);
        cropper.DetectPhotos();

        Assert.Single(cropper.DetectedPhotos);
        Assert.InRange(cropper.DetectedPhotos[0].Width, 480, 520);
        Assert.InRange(cropper.DetectedPhotos[0].Height, 380, 420);
    }

    [Fact]
    public void DetectPhotos_CloselySpaced_ShouldSeparatePhotos()
    {
        string path = Path.Combine(_tempDir, "close_photos.jpg");
        TestImageFactory.CreateCloselySpacedScan(path, gap: 20);

        using var cropper = new PhotoCropperEngine(path);
        cropper.DetectPhotos();

        Assert.Equal(2, cropper.DetectedPhotos.Count);
    }

    [Fact]
    public void DetectPhotos_UltraClosePhotos_ShouldNotMergeIntoOneBigImage()
    {
        string path = Path.Combine(_tempDir, "ultra_close.jpg");
        TestImageFactory.CreateCloselySpacedScan(path, gap: 12);

        using var cropper = new PhotoCropperEngine(path);
        cropper.DetectPhotos();

        Assert.Equal(2, cropper.DetectedPhotos.Count);
        foreach (var photo in cropper.DetectedPhotos)
        {
            Assert.InRange(photo.Width, 470, 530);
            Assert.InRange(photo.Height, 470, 530);
        }
    }

    [Fact]
    public void SetCustomBackgroundFromPixel_ShouldSampleCorrectlyAndOverrideBackground()
    {
        using var cropper = new PhotoCropperEngine(_standardScanPath);

        // Sample at 0, 0 (white background)
        cropper.SetCustomBackgroundFromPixel(0, 0);
        Assert.NotNull(cropper.CustomBackgroundColorHsv);
        var whiteHsv = cropper.CustomBackgroundColorHsv.Value;
        Assert.InRange(whiteHsv.V0, 0, 10);
        Assert.InRange(whiteHsv.V1, 0, 10);
        Assert.InRange(whiteHsv.V2, 245, 256);

        // Sample at 200, 200 (black photo)
        cropper.SetCustomBackgroundFromPixel(200, 200);
        Assert.NotNull(cropper.CustomBackgroundColorHsv);
        var blackHsv = cropper.CustomBackgroundColorHsv.Value;
        Assert.InRange(blackHsv.V2, 0, 10);

        // Reset
        cropper.CustomBackgroundColorHsv = null;
        Assert.Null(cropper.CustomBackgroundColorHsv);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
