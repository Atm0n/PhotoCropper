using PhotoCropper.Core.Models;
using PhotoCropper.Gui.Models;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Gui.Tests.Models;

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
    public void ScanSessionItem_WithSavedCrops_ShouldRestoreCrops()
    {
        var options = new DetectionOptions();
        var savedCrops = new List<PhotoCropper.Core.Workspace.WorkspaceCropData>
        {
            new PhotoCropper.Core.Workspace.WorkspaceCropData { CenterX = 50, CenterY = 50, Width = 20, Height = 20, Angle = 0 }
        };

        // Needs to be IsSaved = true and IsModified = false to bypass detection
        using var item = new ScanSessionItem(_scanPath, options, isSaved: true, isModified: false, savedCrops: savedCrops);
        
        item.SavedCrops.ShouldNotBeNull();
        item.SavedCrops.Count.ShouldBe(1);
        
        var engine = item.Activate();
        item.IsActive.ShouldBeTrue();
        
        // normally the test image has 2 photos, but we injected 1 crop and bypassed detection
        engine.DetectedPhotos.Count.ShouldBe(1);
        item.PhotoCount.ShouldBe(1);
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

    [Fact]
    public void ScanSessionItem_TrackingProperties_ShouldReflectAssignedState()
    {
        var options = new DetectionOptions { BackgroundTolerance = 30.0 };
        using var defaultItem = new ScanSessionItem(_scanPath, options);
        defaultItem.IsSaved.ShouldBeFalse();
        defaultItem.IsModified.ShouldBeTrue();

        using var savedItem = new ScanSessionItem(_scanPath, options, isSaved: true, isModified: false);
        savedItem.IsSaved.ShouldBeTrue();
        savedItem.IsModified.ShouldBeFalse();

        savedItem.IsModified = true;
        savedItem.IsModified.ShouldBeTrue();
    }

    [Fact]
    public void ScanSessionItem_DeactivateIfUnmodified_ShouldOnlyDeactivateWhenNotModified()
    {
        var options = new DetectionOptions { BackgroundTolerance = 30.0 };
        using var modifiedItem = new ScanSessionItem(_scanPath, options, isSaved: false, isModified: true);
        modifiedItem.Activate();
        modifiedItem.IsActive.ShouldBeTrue();
        modifiedItem.DeactivateIfUnmodified();
        modifiedItem.IsActive.ShouldBeTrue(); // Preserved because IsModified == true

        using var unmodifiedItem = new ScanSessionItem(_scanPath, options, isSaved: true, isModified: false);
        unmodifiedItem.Activate();
        unmodifiedItem.IsActive.ShouldBeTrue();
        unmodifiedItem.DeactivateIfUnmodified();
        unmodifiedItem.IsActive.ShouldBeFalse(); // Disposed because IsModified == false
    }

    [Fact]
    public void ScanSessionItem_Metadata_ShouldStoreAndPersistMetadata()
    {
        var options = new DetectionOptions();
        using var item = new ScanSessionItem(_scanPath, options);

        item.Metadata.ShouldNotBeNull();
        item.Metadata.Year.ShouldBeNull();

        item.Metadata.Year = 1965;
        item.Metadata.Description = "Trip to Paris";

        item.Metadata.Year.ShouldBe(1965);
        item.Metadata.Description.ShouldBe("Trip to Paris");
        item.Metadata.HasMetadata.ShouldBeTrue();
    }

    [Fact]
    public void ScanSessionItem_ConcurrentActivation_ShouldBeThreadSafe()
    {
        var options = new DetectionOptions { BackgroundTolerance = 30.0 };
        using var item = new ScanSessionItem(_scanPath, options);

        Parallel.For(0, 10, _ =>
        {
            var engine = item.Activate();
            engine.ShouldNotBeNull();
        });

        item.IsActive.ShouldBeTrue();
        item.Engine.ShouldNotBeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
