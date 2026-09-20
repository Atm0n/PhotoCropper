using Avalonia.Controls;
using System.Runtime.InteropServices;

namespace PhotoCropper.Gui.Services;

internal static class WindowsTaskbarHelper
{
    private const uint FLASHW_ALL = 0x00000003;
    private const uint FLASHW_TIMERNOFG = 0x0000000C;

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    public static void FlashWindow(Window? window, uint count = 5)
    {
        if (!OperatingSystem.IsWindows() || window == null) return;

        try
        {
            var platformHandle = window.TryGetPlatformHandle();
            if (platformHandle == null || platformHandle.Handle == IntPtr.Zero) return;

            var info = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                hwnd = platformHandle.Handle,
                dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
                uCount = count,
                dwTimeout = 0
            };

            FlashWindowEx(ref info);
        }
        catch
        {
            // Non-critical: Ignore platform handle or P/Invoke issues in restricted environments
        }
    }
}
