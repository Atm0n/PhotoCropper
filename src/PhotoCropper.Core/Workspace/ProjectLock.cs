namespace PhotoCropper.Core.Workspace;

/// <summary>
/// Keeps a project from being open in two places at once: each would save its own copy of the session over the
/// other's, losing records (e.g. of the final photos just exported). Held as an open lock file in the project
/// folder; the operating system releases it if the process ends, even by crashing.
/// </summary>
public sealed class ProjectLock : IDisposable
{
    public const string FileName = ".photocropper.lock";

    private readonly FileStream? _stream;

    private ProjectLock(FileStream? stream) => _stream = stream;

    /// <summary>
    /// Takes the project's lock. Returns null if another PhotoCropper (window or command line) holds it. Where
    /// the lock file can't be written at all (a read-only folder), the project opens unlocked.
    /// </summary>
    public static ProjectLock? TryAcquire(string workDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);
        Directory.CreateDirectory(workDirectory);
        string path = Path.Combine(workDirectory, FileName);
        try
        {
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            try
            {
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Only cosmetic.
            }
            return new ProjectLock(stream);
        }
        catch (UnauthorizedAccessException)
        {
            return new ProjectLock(null);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose() => _stream?.Dispose();
}
