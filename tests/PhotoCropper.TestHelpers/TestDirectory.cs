namespace PhotoCropper.TestHelpers;

/// <summary>Cleanup for the temporary folders tests create.</summary>
public static class TestDirectory
{
    /// <summary>
    /// Deletes <paramref name="path"/> and everything in it, if it exists. A file still held open (an image the
    /// OS or a finalizer hasn't released yet) leaves the folder behind rather than failing a test that passed.
    /// </summary>
    public static void TryDelete(string path)
    {
        if (!Directory.Exists(path)) return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Still in use; the OS temp folder is cleaned up eventually.
        }
        catch (UnauthorizedAccessException)
        {
            // A read-only file a test made; same as above.
        }
    }
}
