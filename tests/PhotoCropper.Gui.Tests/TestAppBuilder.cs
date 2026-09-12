using Avalonia;

namespace PhotoCropper.Gui.Tests;

internal static class TestAppBuilder
{
    private static bool _isInitialized;

    public static void EnsureInitialized()
    {
        if (_isInitialized)
        {
            return;
        }

        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .SetupWithoutStarting();

        _isInitialized = true;
    }
}
