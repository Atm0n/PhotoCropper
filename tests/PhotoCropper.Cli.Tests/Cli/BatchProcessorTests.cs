using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Cli.Models;
using PhotoCropper.Cli.Services;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Cli.Tests.Cli;

public sealed class BatchProcessorTests : IDisposable
{
    private readonly string _tempDir;

    public BatchProcessorTests()
    {
        _tempDir = TestImageFactory.CreateTempDirectory("BatchProcessorTests");
    }

    [Fact]
    public void BatchProcessor_Execute_ShouldExtractPhotosToOutputDirectory()
    {
        string inputDir = Path.Combine(_tempDir, "scans");
        string outputDir = Path.Combine(_tempDir, "output");
        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(outputDir);

        string scan1Path = Path.Combine(inputDir, "scan1.jpg");
        string scan2Path = Path.Combine(inputDir, "scan2.jpg");

        TestImageFactory.CreateStandardTwoPhotoScan(scan1Path);
        TestImageFactory.CreateStandardTwoPhotoScan(scan2Path);

        var options = new CliOptions
        {
            OutputDirectory = outputDir,
            Format = "JPEG",
            JpegQuality = 90,
            Tolerance = 25,
            MinAreaFactor = 0.01,
            NonInteractive = true
        };

        int exitCode = BatchProcessor.Execute(options, [scan1Path, scan2Path]);
        exitCode.ShouldBe(0);

        string[] exportedJpegs = Directory.GetFiles(outputDir, "*.jpg");
        exportedJpegs.Length.ShouldBeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void BatchProcessor_UndetectedScans_ShouldGenerateAuditLogAndCopyFiles()
    {
        string detectedScanPath = Path.Combine(_tempDir, "good_scan.jpg");
        using (Mat scan = new(1000, 1000, DepthType.Cv8U, 3))
        {
            scan.SetTo(new MCvScalar(255, 255, 255));
            CvInvoke.Rectangle(scan, new System.Drawing.Rectangle(100, 100, 400, 400), new MCvScalar(0, 0, 0), -1);
            scan.Save(detectedScanPath);
        }

        string emptyScanPath = Path.Combine(_tempDir, "blank_scan.jpg");
        using (Mat scan = new(1000, 1000, DepthType.Cv8U, 3))
        {
            scan.SetTo(new MCvScalar(255, 255, 255)); // Completely blank scan -> 0 photos
            scan.Save(emptyScanPath);
        }

        string outputDir = Path.Combine(_tempDir, "audit_output");
        string isolateDir = Path.Combine(_tempDir, "isolated_undetected");

        var options = new CliOptions
        {
            OutputDirectory = outputDir,
            CopyUndetectedDirectory = isolateDir,
            Tolerance = 25,
            NonInteractive = true
        };

        int exitCode = BatchProcessor.Execute(options, [detectedScanPath, emptyScanPath]);
        exitCode.ShouldBe(0);

        // Verify audit log exists and lists blank scan
        string auditLogFile = Path.Combine(outputDir, "undetected_scans.txt");
        File.Exists(auditLogFile).ShouldBeTrue();
        string auditContent = File.ReadAllText(auditLogFile);
        auditContent.ShouldContain("blank_scan.jpg");

        // Verify isolated copy was copied to isolateDir
        string isolatedFile = Path.Combine(isolateDir, "blank_scan.jpg");
        File.Exists(isolatedFile).ShouldBeTrue();
    }

    [Fact]
    public void BatchProcessor_AutoTuneOption_ShouldRecoverLowContrastScans()
    {
        string lowContrastScanPath = Path.Combine(_tempDir, "low_contrast.jpg");
        TestImageFactory.CreateLowContrastScan(lowContrastScanPath);

        string outputDir = Path.Combine(_tempDir, "autotune_output");

        var options = new CliOptions
        {
            OutputDirectory = outputDir,
            Tolerance = 25, // Standard tolerance fails on low contrast
            AutoTune = true,
            NonInteractive = true
        };

        int exitCode = BatchProcessor.Execute(options, [lowContrastScanPath]);
        exitCode.ShouldBe(0);

        string[] files = Directory.GetFiles(outputDir, "*.jpg");
        files.Length.ShouldBeGreaterThanOrEqualTo(1);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
