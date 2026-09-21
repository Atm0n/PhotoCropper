using PhotoCropper.Core.Models;
using PhotoCropper.Gui.Models;
using PhotoCropper.Gui.Services;
using PhotoCropper.TestHelpers;
using System.Diagnostics.CodeAnalysis;

namespace PhotoCropper.Gui.Tests.Services;

[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Session items are transferred to ScanSessionManager which manages lifecycle and disposes them on Clear/Dispose")]
public sealed class ScanSessionManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _scan1;
    private readonly string _scan2;
    private readonly string _scan3;

    public ScanSessionManagerTests()
    {
        _tempDir = TestImageFactory.CreateTempDirectory("ScanSessionManagerTests");
        _scan1 = Path.Combine(_tempDir, "scan1.png");
        _scan2 = Path.Combine(_tempDir, "scan2.png");
        _scan3 = Path.Combine(_tempDir, "scan3.png");

        TestImageFactory.CreateStandardTwoPhotoScan(_scan1);
        TestImageFactory.CreateStandardTwoPhotoScan(_scan2);
        TestImageFactory.CreateStandardTwoPhotoScan(_scan3);
    }

    [Fact]
    public void ScanSessionManager_InitialState_ShouldBeEmpty()
    {
        using var manager = new ScanSessionManager();

        manager.Count.ShouldBe(0);
        manager.HasScans.ShouldBeFalse();
        manager.CurrentIndex.ShouldBe(0);
        manager.CurrentSession.ShouldBeNull();
        manager.Sessions.ShouldBeEmpty();
        manager.HasUnsavedChanges().ShouldBeFalse();
        manager.GetPendingExportSessions().ShouldBeEmpty();
    }

    [Fact]
    public void ScanSessionManager_AddAndAddRange_ShouldUpdateCountAndNavigation()
    {
        using var manager = new ScanSessionManager();
        var options = new DetectionOptions();

        var item1 = new ScanSessionItem(_scan1, options);
        var item2 = new ScanSessionItem(_scan2, options);

        manager.Add(item1);
        manager.Count.ShouldBe(1);
        manager.HasScans.ShouldBeTrue();
        manager.CurrentIndex.ShouldBe(0);
        manager.CurrentSession.ShouldBe(item1);

        manager.AddRange([item2]);
        manager.Count.ShouldBe(2);
        manager.Sessions[1].ShouldBe(item2);
    }

    [Fact]
    public void ScanSessionManager_MoveNextAndPrevious_ShouldWrapAround()
    {
        using var manager = new ScanSessionManager();
        var options = new DetectionOptions();

        manager.AddRange([
            new ScanSessionItem(_scan1, options),
            new ScanSessionItem(_scan2, options),
            new ScanSessionItem(_scan3, options)
        ]);

        manager.CurrentIndex.ShouldBe(0);

        manager.MoveNext().ShouldBeTrue();
        manager.CurrentIndex.ShouldBe(1);

        manager.MoveNext().ShouldBeTrue();
        manager.CurrentIndex.ShouldBe(2);

        manager.MoveNext().ShouldBeTrue();
        manager.CurrentIndex.ShouldBe(0); // Wrap around to first

        manager.MovePrevious().ShouldBeTrue();
        manager.CurrentIndex.ShouldBe(2); // Wrap around to last

        manager.MovePrevious().ShouldBeTrue();
        manager.CurrentIndex.ShouldBe(1);
    }

    [Fact]
    public void ScanSessionManager_MoveTo_ShouldValidateBounds()
    {
        using var manager = new ScanSessionManager();
        var options = new DetectionOptions();

        manager.AddRange([
            new ScanSessionItem(_scan1, options),
            new ScanSessionItem(_scan2, options)
        ]);

        manager.MoveTo(1).ShouldBeTrue();
        manager.CurrentIndex.ShouldBe(1);

        manager.MoveTo(-1).ShouldBeFalse();
        manager.CurrentIndex.ShouldBe(1);

        manager.MoveTo(5).ShouldBeFalse();
        manager.CurrentIndex.ShouldBe(1);
    }

    [Fact]
    public void ScanSessionManager_Navigation_ShouldDeactivateUnmodifiedSession()
    {
        using var manager = new ScanSessionManager();
        var options = new DetectionOptions();

        var item1 = new ScanSessionItem(_scan1, options, isSaved: true, isModified: false);
        var item2 = new ScanSessionItem(_scan2, options);

        manager.AddRange([item1, item2]);

        // Activate session 1
        item1.Activate();
        item1.IsActive.ShouldBeTrue();

        // Navigate to session 2
        manager.MoveNext();

        // Session 1 was unmodified, so it should be deactivated to free RAM
        item1.IsActive.ShouldBeFalse();
        item1.CachedPhotoCount.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void ScanSessionManager_RemoveCurrent_ShouldAdjustIndexCorrectly()
    {
        using var manager = new ScanSessionManager();
        var options = new DetectionOptions();

        manager.AddRange([
            new ScanSessionItem(_scan1, options),
            new ScanSessionItem(_scan2, options),
            new ScanSessionItem(_scan3, options)
        ]);

        // Move to middle item (index 1)
        manager.MoveTo(1);
        var removed = manager.RemoveCurrent();
        removed.ShouldNotBeNull();
        removed.FilePath.ShouldBe(_scan2);
        manager.Count.ShouldBe(2);
        manager.CurrentIndex.ShouldBe(1); // Now pointing to item 3 (which shifted to index 1)
        manager.CurrentSession?.FilePath.ShouldBe(_scan3);

        // Remove last item (index 1)
        manager.RemoveCurrent();
        manager.Count.ShouldBe(1);
        manager.CurrentIndex.ShouldBe(0); // Clamped back to 0
        manager.CurrentSession?.FilePath.ShouldBe(_scan1);

        // Remove remaining item
        manager.RemoveCurrent();
        manager.Count.ShouldBe(0);
        manager.HasScans.ShouldBeFalse();
        manager.CurrentIndex.ShouldBe(0);
        manager.CurrentSession.ShouldBeNull();
    }

    [Fact]
    public void ScanSessionManager_ReplaceAll_ShouldDisposeOldAndResetIndex()
    {
        using var manager = new ScanSessionManager();
        var options = new DetectionOptions();

        manager.AddRange([
            new ScanSessionItem(_scan1, options),
            new ScanSessionItem(_scan2, options)
        ]);
        manager.MoveTo(1);

        var newSessions = new[] { new ScanSessionItem(_scan3, options) };
        manager.ReplaceAll(newSessions);

        manager.Count.ShouldBe(1);
        manager.CurrentIndex.ShouldBe(0);
        manager.CurrentSession?.FilePath.ShouldBe(_scan3);
    }

    [Fact]
    public void ScanSessionManager_DirtyTracking_ShouldDetectUnsavedAndPending()
    {
        using var manager = new ScanSessionManager();
        var options = new DetectionOptions();

        var item1 = new ScanSessionItem(_scan1, options, isSaved: true, isModified: false);
        var item2 = new ScanSessionItem(_scan2, options, isSaved: false, isModified: true);

        manager.AddRange([item1, item2]);

        manager.HasUnsavedChanges().ShouldBeTrue();
        var pending = manager.GetPendingExportSessions();
        pending.Count.ShouldBe(1);
        pending[0].ShouldBe(item2);

        // Mark item2 as saved and unmodified
        item2.IsSaved = true;
        item2.IsModified = false;

        manager.HasUnsavedChanges().ShouldBeFalse();
        manager.GetPendingExportSessions().ShouldBeEmpty();
    }

    [Fact]
    public void ScanSessionManager_FindByPath_ShouldFindMatchingSessionCaseInsensitive()
    {
        using var manager = new ScanSessionManager();
        var options = new DetectionOptions();

        manager.AddRange([
            new ScanSessionItem(_scan1, options),
            new ScanSessionItem(_scan2, options)
        ]);

        var found = manager.FindByPath(_scan1.ToUpperInvariant());
        found.ShouldNotBeNull();
        found.FilePath.ShouldBe(_scan1);

        var notFound = manager.FindByPath(Path.Combine(_tempDir, "nonexistent.png"));
        notFound.ShouldBeNull();
    }

    [Fact]
    public void ScanSessionManager_Clear_ShouldResetState()
    {
        using var manager = new ScanSessionManager();
        var options = new DetectionOptions();

        manager.AddRange([
            new ScanSessionItem(_scan1, options),
            new ScanSessionItem(_scan2, options)
        ]);

        manager.Clear();
        manager.Count.ShouldBe(0);
        manager.HasScans.ShouldBeFalse();
        manager.CurrentIndex.ShouldBe(0);
        manager.CurrentSession.ShouldBeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
