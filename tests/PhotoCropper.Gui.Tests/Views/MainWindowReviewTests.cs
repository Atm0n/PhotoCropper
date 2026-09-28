using Avalonia.Input;
using PhotoCropper.Core.Models;
using PhotoCropper.Core.Scanning;
using PhotoCropper.Core.Workspace;
using PhotoCropper.TestHelpers;
using System.Diagnostics.CodeAnalysis;

namespace PhotoCropper.Gui.Tests.Views;

[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Test window disposal")]
public sealed class MainWindowReviewTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _scan1;
    private readonly string _scan2;

    public MainWindowReviewTests()
    {
        TestAppBuilder.EnsureInitialized();
        _tempDir = TestImageFactory.CreateTempDirectory("MainWindowReviewTests");
        _scan1 = Path.Combine(_tempDir, "scan1.png");
        _scan2 = Path.Combine(_tempDir, "scan2.png");
        TestImageFactory.CreateStandardTwoPhotoScan(_scan1);
        TestImageFactory.CreateStandardTwoPhotoScan(_scan2);
    }

    [Fact]
    public void ReviewPhotoItem_RecordProperties_ShouldBeCorrect()
    {
        var item = new ReviewPhotoItem(1, 3);
        item.ScanIndex.ShouldBe(1);
        item.PhotoIndex.ShouldBe(3);
    }

    [Fact]
    public void FlattenReviewItems_ShouldMapAllScansAndPhotosCorrectly()
    {
        var options = new DetectionOptions();
        using var s1 = new ScanSessionItem(_scan1, options);
        using var s2 = new ScanSessionItem(_scan2, options);

        s1.Activate();
        s2.Activate();

        s1.PhotoCount.ShouldBe(2);
        s2.PhotoCount.ShouldBe(2);

        var sessions = new List<ScanSessionItem> { s1, s2 };
        var flattened = MainWindow.FlattenReviewItems(sessions, currentIndex: 1, out int initialIndex, selectedPhotoIndex: 1);

        flattened.Count.ShouldBe(4);
        flattened[0].ShouldBe(new ReviewPhotoItem(0, 0));
        flattened[1].ShouldBe(new ReviewPhotoItem(0, 1));
        flattened[2].ShouldBe(new ReviewPhotoItem(1, 0));
        flattened[3].ShouldBe(new ReviewPhotoItem(1, 1));
        initialIndex.ShouldBe(3);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
