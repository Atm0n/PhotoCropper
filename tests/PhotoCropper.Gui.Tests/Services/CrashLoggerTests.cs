using PhotoCropper.Gui.Services;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Gui.Tests.Services;

public sealed class CrashLoggerTests : IDisposable
{
    private readonly string _tempDir = TestImageFactory.CreateTempDirectory("CrashLoggerTests");

    private static InvalidOperationException Thrown()
    {
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    [Fact]
    public void WriteCrashLog_ShouldWriteReportWithSourceAndStackTrace()
    {
        string? path = CrashLogger.WriteCrashLog(Thrown(), "UI thread", _tempDir);

        path.ShouldNotBeNull();
        string report = File.ReadAllText(path);
        report.ShouldContain("Source:     UI thread");
        report.ShouldContain("System.InvalidOperationException: boom");
        report.ShouldContain(nameof(Thrown));
    }

    [Fact]
    public void WriteCrashLog_ShouldKeepOnlyTheMostRecentReports()
    {
        for (int i = 0; i < CrashLogger.MaxLogFiles + 5; i++)
        {
            string old = Path.Combine(_tempDir, $"crash-old-{i:D3}.log");
            File.WriteAllText(old, "old");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-30 + i * 0.1));
        }

        string? newest = CrashLogger.WriteCrashLog(Thrown(), "test", _tempDir);

        Directory.GetFiles(_tempDir, "crash-*.log").Length.ShouldBe(CrashLogger.MaxLogFiles);
        File.Exists(newest).ShouldBeTrue();
    }

    [Fact]
    public void WriteCrashLog_WhenDirectoryIsUnusable_ShouldReturnNullInsteadOfThrowing()
    {
        // A file where the directory should be makes CreateDirectory fail.
        string blocker = Path.Combine(_tempDir, "not-a-directory");
        File.WriteAllText(blocker, "x");

        Should.NotThrow(() => CrashLogger.WriteCrashLog(Thrown(), "test", blocker)).ShouldBeNull();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }
        catch (IOException)
        {
        }
    }
}
