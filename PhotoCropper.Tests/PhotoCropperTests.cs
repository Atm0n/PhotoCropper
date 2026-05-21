using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System.Drawing;

namespace PhotoCropper.Tests;

public class PhotoCropperTests : IDisposable
{
    private readonly string _testImagePath;
    private readonly string _tempDir;

    public PhotoCropperTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"PhotoCropperTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _testImagePath = Path.Combine(_tempDir, "test_scan.jpg");
        CreateDummyScan(_testImagePath);
    }

    private static void CreateDummyScan(string path)
    {
        using Mat scan = new(2000, 2000, DepthType.Cv8U, 3);
        scan.SetTo(new MCvScalar(255, 255, 255)); // White background

        // Photo 1: Straight Black Square
        CvInvoke.Rectangle(scan, new Rectangle(100, 100, 400, 400), new MCvScalar(0, 0, 0), -1);
        
        // Photo 2: Tilted Dark Grey Polygon
        Point[] points = [
            new Point(1000, 1000),
            new Point(1400, 1100),
            new Point(1300, 1500),
            new Point(900, 1400)
        ];
        using var vp = new Emgu.CV.Util.VectorOfPoint(points);
        CvInvoke.FillConvexPoly(scan, vp, new MCvScalar(50, 50, 50));

        scan.Save(path);
    }

    [Fact]
    public void Constructor_ShouldLoadImage()
    {
        using var cropper = new PhotoCropper(_testImagePath);
        Assert.False(cropper.Original.IsEmpty);
        Assert.Equal(2000, cropper.Original.Width);
    }

    [Fact]
    public void DetectPhotos_ShouldFindTwoPhotos()
    {
        using var cropper = new PhotoCropper(_testImagePath);
        cropper.DetectPhotos();
        Assert.True(cropper.DetectedPhotos.Count >= 2);
    }

    [Fact]
    public void SaveDetectedPhotos_ShouldCreateFiles()
    {
        using var cropper = new PhotoCropper(_testImagePath);
        cropper.DetectPhotos();
        int count = cropper.DetectedPhotos.Count;

        cropper.SaveDetectedPhotos();

        string outputDir = Path.Combine(_tempDir, "cropped");
        Assert.True(Directory.Exists(outputDir));
        
        var files = Directory.GetFiles(outputDir, "*.jpg");
        Assert.Equal(count, files.Length);
    }

    [Fact]
    public void SaveDetectedPhotos_PNG_ShouldCreateLosslessFiles()
    {
        using var cropper = new PhotoCropper(_testImagePath);
        cropper.DetectPhotos();
        int count = cropper.DetectedPhotos.Count;

        cropper.SaveDetectedPhotos(null, "PNG");

        string outputDir = Path.Combine(_tempDir, "cropped");
        Assert.True(Directory.Exists(outputDir));
        
        var files = Directory.GetFiles(outputDir, "*.png");
        Assert.Equal(count, files.Length);
    }

    [Fact]
    public void SaveDetectedPhotos_CustomDir_ShouldCreateFiles()
    {
        using var cropper = new PhotoCropper(_testImagePath);
        cropper.DetectPhotos();
        int count = cropper.DetectedPhotos.Count;

        string customDir = Path.Combine(_tempDir, $"custom_export_{Guid.NewGuid()}");

        cropper.SaveDetectedPhotos(customDir, "JPEG", 85);

        Assert.True(Directory.Exists(customDir));
        
        var files = Directory.GetFiles(customDir, "*.jpg");
        Assert.Equal(count, files.Length);
    }

    [Theory]
    [InlineData(1500, 1500, 100, 100, true)]  // Valid background crop
    [InlineData(1950, 1950, 200, 200, true)]  // Partially out of bounds (should be clamped)
    [InlineData(10, 10, 2, 2, false)]         // Too small (should be ignored)
    [InlineData(80, 80, 450, 450, true)]      // Overlaps Photo 1 (should snap to photo)
    public void AddManualCrop_VariousCases(int x, int y, int w, int h, bool expectedAdded)
    {
        using var cropper = new PhotoCropper(_testImagePath);
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
        using var cropper = new PhotoCropper(_testImagePath);
        
        // 1. Manually create a "messy" Mat: 400x400 total, but with a 200x200 black square in the middle of white.
        using Mat messy = new(400, 400, DepthType.Cv8U, 3);
        messy.SetTo(new MCvScalar(255, 255, 255)); // White margin
        CvInvoke.Rectangle(messy, new Rectangle(100, 100, 200, 200), new MCvScalar(0, 0, 0), -1); // Black content
        
        cropper.DetectedPhotos.Add(messy.Clone());
        Assert.Single(cropper.DetectedPhotos);
        Assert.Equal(400, cropper.DetectedPhotos[0].Width);

        // 2. Get refined rect
        Rectangle refined = cropper.GetRefinedCropRect(0);

        // The refinement should have found the 200x200 black square inside.
        // It should be roughly 200x200 (minus 2px shave = 196x196)
        Assert.InRange(refined.Width, 190, 205);
        Assert.InRange(refined.Height, 190, 205);
        
        // 3. Apply it
        cropper.ApplyCropToPhoto(0, refined);
        Assert.Equal(refined.Width, cropper.DetectedPhotos[0].Width);
    }

    [Fact]
    public void RotatePhoto_ShouldSwapDimensions()
    {
        using var cropper = new PhotoCropper(_testImagePath);
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
        using var cropper = new PhotoCropper(_testImagePath);
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
        using var cropper = new PhotoCropper(_testImagePath);
        cropper.DetectPhotos();
        int initialCount = cropper.DetectedPhotos.Count;

        cropper.DeletePhoto(0);

        Assert.Equal(initialCount - 1, cropper.DetectedPhotos.Count);
    }

    [Fact]
    public void DeletePhoto_InvalidIndex_ShouldNotCrash()
    {
        using var cropper = new PhotoCropper(_testImagePath);
        cropper.DetectPhotos();
        int count = cropper.DetectedPhotos.Count;

        // Try deleting out of bounds
        cropper.DeletePhoto(-1);
        cropper.DeletePhoto(999);

        Assert.Equal(count, cropper.DetectedPhotos.Count);
    }

    [Fact]
    public void DetectPhotos_ShouldDetectSuccessfully_WithPhotoInCorner()
    {
        string cornerImgPath = Path.Combine(_tempDir, "corner_scan.jpg");
        using (Mat scan = new(2000, 2000, DepthType.Cv8U, 3))
        {
            scan.SetTo(new MCvScalar(255, 255, 255)); // White background
            
            // Photo 1 in the middle
            CvInvoke.Rectangle(scan, new Rectangle(800, 800, 400, 400), new MCvScalar(0, 0, 0), -1);

            // A solid dark photo in the bottom-right corner (which previously would corrupt background sampling)
            CvInvoke.Rectangle(scan, new Rectangle(1700, 1700, 300, 300), new MCvScalar(20, 20, 20), -1);

            scan.Save(cornerImgPath);
        }

        using var cropper = new PhotoCropper(cornerImgPath);
        cropper.DetectPhotos();
        
        // Both the middle photo and the corner photo should be detected successfully because the 8-point median
        // perimeter sampling rejects the corner photo outlier and correctly identifies the white background!
        Assert.True(cropper.DetectedPhotos.Count >= 2);
    }

    [Fact]
    public void DetectPhotos_ShouldDetectSuccessfully_OnHighResolutionScans()
    {
        string highResImgPath = Path.Combine(_tempDir, "highres_scan.jpg");
        using (Mat scan = new(5000, 5000, DepthType.Cv8U, 3))
        {
            scan.SetTo(new MCvScalar(255, 255, 255)); // White background
            
            // Large Photo in the middle
            CvInvoke.Rectangle(scan, new Rectangle(1000, 1000, 3000, 3000), new MCvScalar(0, 0, 0), -1);

            scan.Save(highResImgPath);
        }

        using var cropper = new PhotoCropper(highResImgPath);
        cropper.DetectPhotos();
        
        // The photo should be detected successfully because the morphological kernel scales with resolution
        Assert.NotEmpty(cropper.DetectedPhotos);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
