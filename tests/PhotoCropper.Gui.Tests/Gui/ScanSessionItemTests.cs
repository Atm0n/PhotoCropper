using PhotoCropper.Core.Models;
using PhotoCropper.Gui.Services;
using PhotoCropper.TestHelpers;
using Shouldly;
using Xunit;

namespace PhotoCropper.Gui.Tests.Gui;

public sealed class ScanSessionItemTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _scanPath;

    public ScanSessionItemTests()
    {
        _tempDir = TestImageFactory.CreateTempDirectory("ScanSessionItemTests");
        _scanPath = Path.Combine(_tempDir, "session_scan.jpg");
        TestImageFactory.CreateStandardTwoPhotoScan(_scanPath);
    }

    [Fact]
    public void ScanSessionItem_InitialState_ShouldBeInactive()
    {
        var options = new DetectionOptions { BackgroundTolerance = 30.0 };
        using var item = new ScanSessionItem(_scanPath, options);

        item.FilePath.ShouldBe(_scanPath);
        item.IsActive.ShouldBeFalse();
        item.Engine.ShouldBeNull();
        item.PhotoCount.ShouldBe(0);
        item.CachedPhotoCount.ShouldBe(0);
    }

    [Fact]
    public void ScanSessionItem_Activate_ShouldInstantiateEngineAndDetectPhotos()
    {
        var options = new DetectionOptions { BackgroundTolerance = 30.0 };
        using var item = new ScanSessionItem(_scanPath, options);

        var engine = item.Activate();

        item.IsActive.ShouldBeTrue();
        item.Engine.ShouldNotBeNull();
        engine.ShouldBeSameAs(item.Engine);
        item.PhotoCount.ShouldBeGreaterThanOrEqualTo(2);
        item.CachedPhotoCount.ShouldBe(item.PhotoCount);
    }

    [Fact]
    public void ScanSessionItem_Deactivate_ShouldDisposeEngineAndRetainPhotoCount()
    {
        var options = new DetectionOptions { BackgroundTolerance = 30.0 };
        using var item = new ScanSessionItem(_scanPath, options);

        item.Activate();
        int detectedCount = item.PhotoCount;
        detectedCount.ShouldBeGreaterThanOrEqualTo(2);

        item.Deactivate();

        item.IsActive.ShouldBeFalse();
        item.Engine.ShouldBeNull();
        item.PhotoCount.ShouldBe(detectedCount);
        item.CachedPhotoCount.ShouldBe(detectedCount);
    }

    [Fact]
    public void ScanSessionItem_Reactivate_ShouldRestoreEngine()
    {
        var options = new DetectionOptions { BackgroundTolerance = 30.0 };
        using var item = new ScanSessionItem(_scanPath, options);

        item.Activate();
        item.Deactivate();

        var engine2 = item.Activate();
        item.IsActive.ShouldBeTrue();
        engine2.ShouldNotBeNull();
        item.PhotoCount.ShouldBeGreaterThanOrEqualTo(2);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
