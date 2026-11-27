namespace PhotoCropper.Gui.Tests.Views;

public sealed class MainWindowExportTests
{
    [Fact]
    public void FormatFailedScanListShouldListFileNamesWithErrors()
    {
        (string, string)[] failures =
        [
            (Path.Combine("work", "RawScans", "scan_0001.png"), "Access denied"),
            (Path.Combine("work", "RawScans", "scan_0002.png"), "Disk full")
        ];

        string text = MainWindow.FormatFailedScanList(failures);

        text.ShouldBe("• scan_0001.png: Access denied\n• scan_0002.png: Disk full");
    }

    [Fact]
    public void FormatFailedScanListWhenOverLimitShouldTruncateAndShowRemainingCount()
    {
        var failures = Enumerable.Range(1, 12)
            .Select(i => ($"scan_{i:D4}.png", "Error"))
            .ToList();

        string text = MainWindow.FormatFailedScanList(failures, maxShown: 10);

        string[] lines = text.Split('\n');
        lines.Length.ShouldBe(11);
        lines[^1].ShouldBe("… (+2)");
    }
}
