using Avalonia;
using Avalonia.Controls;
using System.Diagnostics;
using System.ComponentModel;

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
        catch (Exception ex) when (IsLaunchFailure(ex))
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
        catch (Exception ex) when (IsLaunchFailure(ex))
        {
            // Suppress launch failures in headless/sandboxed environments
        }
    }

    /// <summary>No browser or handler to open the link with, or the platform can't start one.</summary>
    private static bool IsLaunchFailure(Exception ex) =>
        ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException;
}
