namespace PhotoCropperGui.Services;

internal static class CoordinateMapper
{
    public static Avalonia.Rect ComputeNormalizedRect(Avalonia.Point p1, Avalonia.Point p2)
    {
        return new Avalonia.Rect(
            Math.Min(p1.X, p2.X),
            Math.Min(p1.Y, p2.Y),
            Math.Abs(p1.X - p2.X),
            Math.Abs(p1.Y - p2.Y)
        );
    }

    public static System.Drawing.Rectangle MapUiRectToImageRect(
        Avalonia.Rect uiRect,
        Avalonia.Rect imageBoundsInControl,
        System.Drawing.Size originalImageSize)
    {
        if (imageBoundsInControl.Width <= 0 || imageBoundsInControl.Height <= 0)
        {
            return System.Drawing.Rectangle.Empty;
        }

        double scaleX = originalImageSize.Width / imageBoundsInControl.Width;
        double scaleY = originalImageSize.Height / imageBoundsInControl.Height;

        int x = (int)((uiRect.X - imageBoundsInControl.X) * scaleX);
        int y = (int)((uiRect.Y - imageBoundsInControl.Y) * scaleY);
        int w = (int)(uiRect.Width * scaleX);
        int h = (int)(uiRect.Height * scaleY);

        var rect = new System.Drawing.Rectangle(x, y, w, h);
        rect.Intersect(new System.Drawing.Rectangle(System.Drawing.Point.Empty, originalImageSize));
        return rect;
    }

    public static System.Drawing.Point MapUiPointToImagePixel(
        Avalonia.Point uiPoint,
        Avalonia.Rect imageBoundsInControl,
        System.Drawing.Size originalImageSize)
    {
        if (imageBoundsInControl.Width <= 0 || imageBoundsInControl.Height <= 0)
        {
            return System.Drawing.Point.Empty;
        }

        double scaleX = originalImageSize.Width / imageBoundsInControl.Width;
        double scaleY = originalImageSize.Height / imageBoundsInControl.Height;

        int x = (int)Math.Clamp((uiPoint.X - imageBoundsInControl.X) * scaleX, 0, originalImageSize.Width - 1);
        int y = (int)Math.Clamp((uiPoint.Y - imageBoundsInControl.Y) * scaleY, 0, originalImageSize.Height - 1);

        return new System.Drawing.Point(x, y);
    }
}
