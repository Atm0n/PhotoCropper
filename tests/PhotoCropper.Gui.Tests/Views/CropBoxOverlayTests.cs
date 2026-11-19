using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PhotoCropper.Core.Models;
using PhotoCropper.Gui.Controls;

namespace PhotoCropper.Gui.Tests.Views;

public sealed class CropBoxOverlayTests
{
    // Axis-aligned 200x100 box centred at (500, 400): corners (400,350) (600,350) (600,450) (400,450).
    private static CropCandidate Box() => new()
    {
        Rotated = new RotatedBox(new System.Drawing.PointF(500, 400), new System.Drawing.SizeF(200, 100), 0),
        Area = 200 * 100,
        ShapePoints = [],
        Rect = new System.Drawing.Rectangle(400, 350, 200, 100),
    };

    [Fact]
    public void Geometry_PutsDeleteBadgeOnTopRightCorner()
    {
        var geometry = CropBoxGeometry.For(Box(), new System.Drawing.Size(1000, 800), overlayScale: 1.0);

        geometry.DeleteBadge.ShouldBe(new System.Drawing.PointF(600, 350));
        geometry.TopMid.ShouldBe(new System.Drawing.PointF(500, 350));
    }

    [Fact]
    public void Geometry_RotationHandleDistanceScalesWithOverlay()
    {
        var size = new System.Drawing.Size(1000, 800);

        CropBoxGeometry.For(Box(), size, overlayScale: 1.0).RotationHandle.ShouldBe(new System.Drawing.PointF(500, 290));
        CropBoxGeometry.For(Box(), size, overlayScale: 2.0).RotationHandle.ShouldBe(new System.Drawing.PointF(500, 230));
    }

    [Fact]
    public void Render_DrawsBadgesAndOutlineOverTheScan()
    {
        TestAppBuilder.EnsureInitialized();

        // Displayed at half the scan size, so scan pixel (x, y) lands on (x / 2, y / 2).
        var overlay = new CropBoxOverlay
        {
            Candidates = [Box()],
            ScanSize = new Size(1000, 800),
            OverlayScale = 0.5,
            BoxColor = Colors.Lime,
        };
        overlay.Measure(new Size(500, 400));
        overlay.Arrange(new Rect(0, 0, 500, 400));

        using var bitmap = new RenderTargetBitmap(new PixelSize(500, 400));
        bitmap.Render(overlay);

        Pixel(bitmap, 300, 183).ShouldBe(Colors.Red, "delete badge on the top-right corner");
        Pixel(bitmap, 250, 160).ShouldBe(Colors.Blue, "rotation handle above the top edge");
        Pixel(bitmap, 200, 200).ShouldBe(Colors.Lime, "outline on the left edge");
        Pixel(bitmap, 250, 200).A.ShouldBe((byte)0, "inside the box stays transparent");
    }

    private static Color Pixel(Bitmap bitmap, int x, int y)
    {
        var buffer = new byte[4];
        nint native = System.Runtime.InteropServices.Marshal.AllocHGlobal(buffer.Length);
        try
        {
            bitmap.CopyPixels(new PixelRect(x, y, 1, 1), native, buffer.Length, 4);
            System.Runtime.InteropServices.Marshal.Copy(native, buffer, 0, buffer.Length);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(native);
        }
        // Bgra8888, premultiplied; the sampled pixels are opaque or fully transparent.
        return Color.FromArgb(buffer[3], buffer[2], buffer[1], buffer[0]);
    }
}
