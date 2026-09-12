using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Core.Models;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Core.Tests.Engine;

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

        options1.BackgroundTolerance.ShouldBe(45);
        options1.CannyLowThreshold.ShouldBe(10);

        options2.BackgroundTolerance.ShouldBe(15);
        options2.CannyLowThreshold.ShouldBe(35);
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

        engine1.BackgroundTolerance.ShouldBe(60);
        engine1.MinAreaFactor.ShouldBe(0.05);
        engine1.AutoOrientPhotos.ShouldBeFalse();

        // Engine2 retains original defaults
        engine2.BackgroundTolerance.ShouldBe(30);
        engine2.MinAreaFactor.ShouldBe(0.01);
        engine2.AutoOrientPhotos.ShouldBeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
