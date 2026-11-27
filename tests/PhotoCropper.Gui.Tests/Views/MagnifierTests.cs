using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using OpenCvSharp;
using PhotoCropper.Core.Models;
using PhotoCropper.Gui.Controls;
using PhotoCropper.Gui.Services;

namespace PhotoCropper.Gui.Tests.Views;

public sealed class MagnifierTests
{
    // A 1000x800 scan, black left of x = 500 and white from it.
    private static Bitmap Scan()
    {
        using var mat = new Mat(800, 1000, MatType.CV_8UC3, Scalar.All(0));
        mat[new OpenCvSharp.Rect(500, 0, 500, 800)].SetTo(Scalar.All(255));
        return MatBitmapConverter.ToAvaloniaBitmap(mat);
    }

    private static CropCandidate BoxFromX(float left) => new()
    {
        Rotated = new RotatedBox(new System.Drawing.PointF((left + 900) / 2, 400), new System.Drawing.SizeF(900 - left, 400), 0),
        Area = (900 - left) * 400,
        ShapePoints = [],
        Rect = new System.Drawing.Rectangle((int)left, 200, (int)(900 - left), 400),
    };

    private static RenderTargetBitmap Render(Magnifier magnifier)
    {
        magnifier.Width = 200;
        magnifier.Height = 200;
        magnifier.Measure(new Avalonia.Size(200, 200));
        magnifier.Arrange(new Avalonia.Rect(0, 0, 200, 200));
        var bitmap = new RenderTargetBitmap(new PixelSize(200, 200));
        bitmap.Render(magnifier);
        return bitmap;
    }

    [Fact]
    public void RenderMagnifiesTheScanAroundThePointWithTheBoxAsAThinLine()
    {
        TestAppBuilder.EnsureInitialized();
        using var scan = Scan();
        var magnifier = new Magnifier();

        // Zoom 4: the 200-unit view shows scan x 475..525, so the scan's edge at x = 500 lands on view x = 100 and the
        // box's left side at x = 490 on view x = 60.
        magnifier.Show(scan, new Avalonia.Point(500, 400), zoom: 4, [BoxFromX(490)], Colors.Lime);
        using var bitmap = Render(magnifier);

        Pixel(bitmap, 30, 30).ShouldBe(Colors.Black, "scan left of its edge");
        Pixel(bitmap, 96, 30).ShouldBe(Colors.Black, "one scan pixel left of the edge is 4 view pixels wide");
        Pixel(bitmap, 104, 30).ShouldBe(Colors.White, "scan right of its edge");
        var line = Pixel(bitmap, 60, 30);
        line.G.ShouldBeGreaterThan((byte)100, "box side drawn over the scan");
        Pixel(bitmap, 64, 30).ShouldBe(Colors.Black, "the box side is thin, the scan next to it stays visible");
        Pixel(bitmap, 100 + 15, 100).ShouldBe(Colors.White, "crosshair arm");
    }

    [Fact]
    public void RenderAfterItsImageWasDisposedShouldNotThrow()
    {
        // The crash from the Finish editor: a preview rendered again mid-drag disposes the bitmap the magnifier shows.
        TestAppBuilder.EnsureInitialized();
        var scan = Scan();
        var magnifier = new Magnifier();
        magnifier.Show(scan, new Avalonia.Point(500, 400), zoom: 4, null, Colors.Lime);
        scan.Dispose();

        Should.NotThrow(() => Render(magnifier).Dispose());
    }

    [Fact]
    public void ReplaceSourceShouldDrawTheNewRendering()
    {
        TestAppBuilder.EnsureInitialized();
        using var old = Scan();
        using var white = new Mat(800, 1000, MatType.CV_8UC3, Scalar.All(255));
        using var replaced = MatBitmapConverter.ToAvaloniaBitmap(white);
        var magnifier = new Magnifier();
        magnifier.Show(old, new Avalonia.Point(300, 400), zoom: 4, null, Colors.Lime);

        magnifier.ReplaceSource(replaced);
        using var bitmap = Render(magnifier);

        Pixel(bitmap, 30, 30).ShouldBe(Colors.White, "drawn from the new, all-white image");
    }

    [Fact]
    public void RenderPastTheScanEdgeShowsTheBackdrop()
    {
        TestAppBuilder.EnsureInitialized();
        using var scan = Scan();
        var magnifier = new Magnifier();

        magnifier.Show(scan, new Avalonia.Point(1000, 0), zoom: 2, boxes: null, Colors.Lime);
        using var bitmap = Render(magnifier);

        Pixel(bitmap, 50, 150).ShouldBe(Colors.White, "the scan's top-right corner, below and left of the point");
        Pixel(bitmap, 150, 50).ShouldBe(Color.FromRgb(32, 32, 32), "outside the scan");
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
        return Color.FromArgb(buffer[3], buffer[2], buffer[1], buffer[0]);
    }
}
