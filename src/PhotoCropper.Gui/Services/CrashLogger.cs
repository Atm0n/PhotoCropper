using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace PhotoCropper.Gui.Services;

/// <summary>
/// Writes crash reports for unhandled exceptions to a log folder, so failures users hit can be diagnosed.
/// Never throws: it runs while the app is already failing.
/// </summary>
internal static class CrashLogger
{
    /// <summary>How many crash reports to keep; older ones are deleted.</summary>
    internal const int MaxLogFiles = 20;

    public static string DefaultLogDirectory
    {
        get
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                root = !string.IsNullOrWhiteSpace(home) ? home : AppContext.BaseDirectory;
            }
            return Path.Combine(root, "PhotoCropper", "logs");
        }
    }

    /// <summary>Writes a crash report and returns its path, or null if it could not be written.</summary>
    /// <param name="source">Where the exception was caught (e.g. "UI thread"), included in the report.</param>
    public static string? WriteCrashLog(Exception exception, string source, string? directory = null)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(exception);
            string dir = directory ?? DefaultLogDirectory;
            Directory.CreateDirectory(dir);

            DateTime now = DateTime.UtcNow;
            string path = Path.Combine(dir, $"crash-{now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}-{Environment.ProcessId}.log");
            File.WriteAllText(path, BuildReport(exception, source, now), Encoding.UTF8);

            // The report is written; failing to tidy old ones must not hide its path from the user.
            try
            {
                PruneOldLogs(dir);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning($"[PhotoCropper] Could not prune old crash logs: {ex.Message}");
            }

            return path;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"[PhotoCropper] Could not write crash log: {ex.Message}. Original error: {exception}");
            return null;
        }
    }

    internal static string BuildReport(Exception exception, string source, DateTime utcNow)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"PhotoCropper crash report");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Time (UTC): {utcNow:O}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Version:    {typeof(CrashLogger).Assembly.GetName().Version}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"OS:         {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Runtime:    {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Source:     {source}");
        sb.AppendLine();
        sb.AppendLine(exception.ToString());
        return sb.ToString();
    }

    private static void PruneOldLogs(string dir)
    {
        var stale = new DirectoryInfo(dir)
            .GetFiles("crash-*.log")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(MaxLogFiles);

        foreach (var file in stale)
        {
            try
            {
                file.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort: skip files we can't delete (in use, read-only, no permission).
            }
        }
    }
}
