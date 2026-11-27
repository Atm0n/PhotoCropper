using PhotoCropper.Core.Models;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Gui.Tests.Services;

public sealed class ScanSessionManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _scan1;
    private readonly string _scan2;
    private readonly string _scan3;

    /// <summary>New sessions for the scans, for a manager to own.</summary>
    private static List<ScanSessionItem> Sessions(DetectionOptions options, params string[] paths) =>
        [.. paths.Select(path => new ScanSessionItem(path, options))];

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
    public void MoveShouldReorderAndKeepTheSameScanSelected()
    {
        using var manager = new ScanSessionManager();
        manager.AddRange(Sessions(new DetectionOptions(), _scan1, _scan2, _scan3));
        manager.MoveTo(2);

        manager.Move(2, 0);

        manager.Sessions.Select(s => Path.GetFileName(s.FilePath)).ShouldBe(["scan3.png", "scan1.png", "scan2.png"]);
        manager.CurrentSession!.FilePath.ShouldBe(_scan3);
    }

    [Fact]
    public void ScanSessionManagerInitialStateShouldBeEmpty()
    {
        using var manager = new ScanSessionManager();

        manager.Count.ShouldBe(0);
        manager.HasScans.ShouldBeFalse();
        manager.CurrentIndex.ShouldBe(0);
        manager.CurrentSession.ShouldBeNull();
        manager.Sessions.ShouldBeEmpty();
    }

    [Fact]
    public void ScanSessionManagerAddAndAddRangeShouldUpdateCountAndNavigation()
    {
        using var manager = new ScanSessionManager();
        var options = new DetectionOptions();

        // The manager owns the sessions once they are added.
        var sessions = new[] { new ScanSessionItem(_scan1, options), new ScanSessionItem(_scan2, options) };
        var (item1, item2) = (sessions[0], sessions[1]);

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
    public void ScanSessionManagerMoveNextAndPreviousShouldClamp()
    {
        using var manager = new ScanSessionManager();
        var options = new DetectionOptions();

        manager.AddRange(Sessions(options, _scan1, _scan2, _scan3));

        manager.CurrentIndex.ShouldBe(0);

        manager.MoveNext().ShouldBeTrue();
        manager.CurrentIndex.ShouldBe(1);

        manager.MoveNext().ShouldBeTrue();
        manager.CurrentIndex.ShouldBe(2);

        manager.MoveNext().ShouldBeFalse();
        manager.CurrentIndex.ShouldBe(2); // Clamped at last

        manager.MovePrevious().ShouldBeTrue();
        manager.CurrentIndex.ShouldBe(1);

        manager.MovePrevious().ShouldBeTrue();
        manager.CurrentIndex.ShouldBe(0);

        manager.MovePrevious().ShouldBeFalse();
        manager.CurrentIndex.ShouldBe(0); // Clamped at first
    }

    [Fact]
    public void ScanSessionManagerMoveToShouldValidateBounds()
    {
        using var manager = new ScanSessionManager();
        var options = new DetectionOptions();

        manager.AddRange(Sessions(options, _scan1, _scan2));

        manager.MoveTo(1).ShouldBeTrue();
        manager.CurrentIndex.ShouldBe(1);

        manager.MoveTo(-1).ShouldBeFalse();
        manager.CurrentIndex.ShouldBe(1);

        manager.MoveTo(5).ShouldBeFalse();
        manager.CurrentIndex.ShouldBe(1);
    }

    [Fact]
    public void ScanSessionManagerNavigationShouldDeactivateUnmodifiedSession()
    {
        using var manager = new ScanSessionManager();
        var options = new DetectionOptions();

        // The manager owns the sessions once they are added.
        var sessions = new[] { new ScanSessionItem(_scan1, options, isSaved: true, isModified: false), new ScanSessionItem(_scan2, options) };
        var (item1, item2) = (sessions[0], sessions[1]);

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
    public void ScanSessionManagerSessionDeactivatedShouldFireWithCorrectIndexWhenUnmodifiedSessionDeactivated()
    {
        using var manager = new ScanSessionManager();
        var options = new DetectionOptions();

        // The manager owns the sessions once they are added.
        var sessions = new[] { new ScanSessionItem(_scan1, options, isSaved: true, isModified: false), new ScanSessionItem(_scan2, options) };
        var (item1, item2) = (sessions[0], sessions[1]);

        manager.AddRange([item1, item2]);
        item1.Activate();

        var firedIndices = new List<int>();
        manager.SessionDeactivated += idx => firedIndices.Add(idx);

        // Navigate away from session 0 (unmodified) → event should fire for index 0
        manager.MoveNext();

        firedIndices.Count.ShouldBe(1);
        firedIndices[0].ShouldBe(0);
    }

    [Fact]
    public void ScanSessionManagerSessionDeactivatedShouldNotFireForModifiedSession()
    {
        using var manager = new ScanSessionManager();
        var options = new DetectionOptions();

        // item1 is modified — TryDeactivateIfUnmodified will skip it
        // The manager owns the sessions once they are added.
        var sessions = new[] { new ScanSessionItem(_scan1, options, isSaved: false, isModified: true), new ScanSessionItem(_scan2, options) };
        var (item1, item2) = (sessions[0], sessions[1]);

        manager.AddRange([item1, item2]);
        item1.Activate();

        var firedIndices = new List<int>();
        manager.SessionDeactivated += idx => firedIndices.Add(idx);

        manager.MoveNext();

        // Modified session stays active, event must not fire
        firedIndices.ShouldBeEmpty();
        item1.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void ScanSessionManagerRemoveCurrentShouldAdjustIndexCorrectly()
    {
        using var manager = new ScanSessionManager();
        var options = new DetectionOptions();

        manager.AddRange(Sessions(options, _scan1, _scan2, _scan3));

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
    public void ScanSessionManagerReplaceAllShouldDisposeOldAndResetIndex()
    {
        using var manager = new ScanSessionManager();
        var options = new DetectionOptions();

        manager.AddRange(Sessions(options, _scan1, _scan2));
        manager.MoveTo(1);

        var newSessions = new[] { new ScanSessionItem(_scan3, options) };
        manager.ReplaceAll(newSessions);

        manager.Count.ShouldBe(1);
        manager.CurrentIndex.ShouldBe(0);
        manager.CurrentSession?.FilePath.ShouldBe(_scan3);
    }

    [Fact]
    public void ScanSessionManagerSessionModifiedShouldReportEveryEditOfATrackedScan()
    {
        using var manager = new ScanSessionManager();
        var options = new DetectionOptions();
        // The manager owns the sessions once they are added.
        var sessions = new[] { new ScanSessionItem(_scan1, options, isSaved: true, isModified: false), new ScanSessionItem(_scan2, options, isSaved: false, isModified: true) };
        var (item1, item2) = (sessions[0], sessions[1]);
        var reported = new List<ScanSessionItem>();
        manager.SessionModified += reported.Add;

        manager.AddRange([item1, item2]);
        reported.ShouldBeEmpty(); // constructing a session is not an edit

        item1.IsModified = true;
        item1.IsModified = true; // each edit counts, even when already modified
        item2.IsModified = false;

        reported.ShouldBe([item1, item1]);
    }

    [Fact]
    public void ScanSessionManagerSessionModifiedShouldStopForRemovedScans()
    {
        using var manager = new ScanSessionManager();
        var options = new DetectionOptions();
        // The manager owns the sessions once they are added.
        var sessions = new[] { new ScanSessionItem(_scan1, options) };
        var item = sessions[0];
        var reported = new List<ScanSessionItem>();
        manager.SessionModified += reported.Add;
        manager.Add(item);

        using var removed = manager.RemoveAt(0);
        item.IsModified = true;

        reported.ShouldBeEmpty();
    }

    [Fact]
    public void ScanSessionManagerFindByPathShouldFindMatchingSessionCaseInsensitive()
    {
        using var manager = new ScanSessionManager();
        var options = new DetectionOptions();

        manager.AddRange(Sessions(options, _scan1, _scan2));

        var found = manager.FindByPath(_scan1.ToUpperInvariant());
        found.ShouldNotBeNull();
        found.FilePath.ShouldBe(_scan1);

        var notFound = manager.FindByPath(Path.Combine(_tempDir, "nonexistent.png"));
        notFound.ShouldBeNull();
    }

    [Fact]
    public void ScanSessionManagerClearShouldResetState()
    {
        using var manager = new ScanSessionManager();
        var options = new DetectionOptions();

        manager.AddRange(Sessions(options, _scan1, _scan2));

        manager.Clear();
        manager.Count.ShouldBe(0);
        manager.HasScans.ShouldBeFalse();
        manager.CurrentIndex.ShouldBe(0);
        manager.CurrentSession.ShouldBeNull();
    }

    public void Dispose()
    {
        TestDirectory.TryDelete(_tempDir);
    }
}
