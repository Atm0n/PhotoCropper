using Avalonia;

namespace PhotoCropper.Gui.Tests;

internal static class TestAppBuilder
{
    private static readonly object _syncLock = new();
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

            _isInitialized = true;
        }
    }
}
