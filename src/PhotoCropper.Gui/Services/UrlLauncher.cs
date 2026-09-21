using Avalonia;
using Avalonia.Controls;
using System.Diagnostics;

namespace PhotoCropper.Gui.Services;

internal static class UrlLauncher
{
    public static async Task OpenUrlAsync(Visual? visual, Uri? uri)
    {
        if (uri == null) return;

        try
        {
            var topLevel = visual != null ? TopLevel.GetTopLevel(visual) : null;
            if (topLevel?.Launcher != null)
            {
                await topLevel.Launcher.LaunchUriAsync(uri);
                return;
            }
        }
        catch
        {
            // Fallback to shell process start
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch
        {
            // Suppress launch failures in headless/sandboxed environments
        }
    }
}
