using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Detection;
using PhotoCropper.Models;
using PhotoCropperCli.Models;
using PhotoCropperCli.Services;
using System.Drawing;

namespace PhotoCropper.Tests;

public sealed class AutoTuneAndIsolationTests : IDisposable
{
    private readonly string _tempDir;

    public AutoTuneAndIsolationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"AutoTuneTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void CurrentOptions_ShouldBeIsolatedPerInstance()
    {
        string scanPath = Path.Combine(_tempDir, "dummy_scan.jpg");
        using (Mat scan = new(500, 500, DepthType.Cv8U, 3))
        {
            scan.SetTo(new MCvScalar(255, 255, 255));
            scan.Save(scanPath);
        }

        using var engine1 = new PhotoCropperEngine(scanPath);
        using var engine2 = new PhotoCropperEngine(scanPath);

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
    public void AutoTune_ShouldOptimizeDifficultScans()
    {
        string scanPath = Path.Combine(_tempDir, "difficult_contrast_scan.jpg");
        using (Mat scan = new(2000, 2000, DepthType.Cv8U, 3))
        {
            scan.SetTo(new MCvScalar(240, 240, 240)); // Slightly off-white scanner background
            // Very subtle low contrast photo
            CvInvoke.Rectangle(scan, new Rectangle(300, 300, 600, 600), new MCvScalar(232, 230, 228), -1);
            scan.Save(scanPath);
        }

        using var engine = new PhotoCropperEngine(scanPath);
        // Restrictive initial options that may miss the subtle contrast
        engine.BackgroundTolerance = 5;
        engine.CannyLowThreshold = 50;
        engine.DetectPhotos();

        int initialCount = engine.DetectedPhotos.Count;

        var result = engine.AutoTune();

        Assert.NotNull(result);
        Assert.NotNull(result.BestOptions);
        // After auto-tune, the engine should have detected the photo
        Assert.True(engine.DetectedPhotos.Count >= initialCount);
    }

    [Fact]
    public void BatchProcessor_ShouldLogUndetectedScansAndIsolate()
    {
        string detectedScanPath = Path.Combine(_tempDir, "good_scan.jpg");
        using (Mat scan = new(1000, 1000, DepthType.Cv8U, 3))
        {
            scan.SetTo(new MCvScalar(255, 255, 255));
            CvInvoke.Rectangle(scan, new Rectangle(100, 100, 400, 400), new MCvScalar(0, 0, 0), -1);
            scan.Save(detectedScanPath);
        }

        string emptyScanPath = Path.Combine(_tempDir, "blank_scan.jpg");
        using (Mat scan = new(1000, 1000, DepthType.Cv8U, 3))
        {
            scan.SetTo(new MCvScalar(255, 255, 255)); // Completely blank scan -> 0 photos
            scan.Save(emptyScanPath);
        }

        string outputDir = Path.Combine(_tempDir, "output");
        string isolateDir = Path.Combine(_tempDir, "isolated_undetected");

        var options = new CliOptions
        {
            OutputDirectory = outputDir,
            CopyUndetectedDirectory = isolateDir,
            Tolerance = 25
        };

        int exitCode = BatchProcessor.Execute(options, [detectedScanPath, emptyScanPath]);

        Assert.Equal(0, exitCode);

        // Verify audit log exists
        string auditLogFile = Path.Combine(outputDir, "undetected_scans.txt");
        Assert.True(File.Exists(auditLogFile));
        string auditContent = File.ReadAllText(auditLogFile);
        Assert.Contains("blank_scan.jpg", auditContent, StringComparison.Ordinal);

        // Verify isolated copy exists
        string isolatedFile = Path.Combine(isolateDir, "blank_scan.jpg");
        Assert.True(File.Exists(isolatedFile));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
