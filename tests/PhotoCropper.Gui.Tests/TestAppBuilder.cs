using Avalonia;

namespace PhotoCropper.Gui.Tests;

internal static class TestAppBuilder
{
    private static readonly Lock _syncLock = new();
    private static bool _isInitialized;

    public static void EnsureInitialized()
    {
        if (_isInitialized)
        {
            return;
        }

        lock (_syncLock)
        {
            if (_isInitialized)
            {
                return;
            }

            // Setup installs Avalonia's SynchronizationContext on the calling thread. Its dispatcher never runs in
            // tests, so an await in the test that happened to set Avalonia up would try to resume on it and hang (seen
            // when one test class runs alone and an async test goes first). Keep the test thread's own context.
            var previousContext = SynchronizationContext.Current;
            try
            {
                AppBuilder.Configure<App>()
                    .UsePlatformDetect()
                    .SetupWithoutStarting();
            }
            catch (InvalidOperationException)
            {
                // AppBuilder has already been setup by another runner/test instance
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }

            _isInitialized = true;
        }
    }
}
