using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Models;
using PhotoCropper.Tests.Helpers;

namespace PhotoCropper.Tests.Engine;

public sealed class ScanIsolationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _scanPath;

    public ScanIsolationTests()
    {
        _tempDir = TestImageFactory.CreateTempDirectory("IsolationTests");
        _scanPath = Path.Combine(_tempDir, "scan.jpg");
        using Mat scan = new(500, 500, DepthType.Cv8U, 3);
        scan.SetTo(new MCvScalar(255, 255, 255));
        scan.Save(_scanPath);
    }

    [Fact]
    public void CurrentOptions_ShouldBeIndependentSnapshots()
    {
        using var engine1 = new PhotoCropperEngine(_scanPath);
        using var engine2 = new PhotoCropperEngine(_scanPath);

        engine1.BackgroundTolerance = 45;
        engine1.CannyLowThreshold = 10;

        engine2.BackgroundTolerance = 15;
        engine2.CannyLowThreshold = 35;

        var options1 = engine1.CurrentOptions;
        var options2 = engine2.CurrentOptions;

        Assert.Equal(45, options1.BackgroundTolerance);
        Assert.Equal(10, options1.CannyLowThreshold);

        Assert.Equal(15, options2.BackgroundTolerance);
        Assert.Equal(35, options2.CannyLowThreshold);
    }

    [Fact]
    public void ApplyOptions_ShouldOnlyModifyTargetEngine()
    {
        using var engine1 = new PhotoCropperEngine(_scanPath);
        using var engine2 = new PhotoCropperEngine(_scanPath);

        var customOptions = new DetectionOptions
        {
            BackgroundTolerance = 60,
            MinAreaFactor = 0.05,
            AutoOrientPhotos = false
        };

        engine1.ApplyOptions(customOptions);

        Assert.Equal(60, engine1.BackgroundTolerance);
        Assert.Equal(0.05, engine1.MinAreaFactor);
        Assert.False(engine1.AutoOrientPhotos);

        // Engine2 retains original defaults
        Assert.Equal(30, engine2.BackgroundTolerance);
        Assert.Equal(0.01, engine2.MinAreaFactor);
        Assert.True(engine2.AutoOrientPhotos);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
