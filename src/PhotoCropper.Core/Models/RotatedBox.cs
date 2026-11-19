using System.Drawing;

namespace PhotoCropper.Core.Models;

/// <summary>
/// A rotated rectangle in scan pixels: a photo's box. Uses <see cref="System.Drawing"/> types so the models, the
/// session file and the GUI don't depend on an OpenCV wrapper; <see cref="Common.CvInterop"/> converts it at the
/// OpenCV boundary, and its corners and bounds are computed by OpenCV itself, so they match what OpenCV crops.
/// </summary>
/// <param name="Center">Centre of the box.</param>
/// <param name="Size">Width and height before rotation.</param>
/// <param name="Angle">Rotation in degrees, as OpenCV measures it.</param>
public readonly record struct RotatedBox(PointF Center, SizeF Size, float Angle)
{
    /// <summary>The four corners, in OpenCV's order (bottom-left, top-left, top-right, bottom-right for a box at 0°).</summary>
    public PointF[] GetVertices() => [.. Common.CvInterop.ToCv(this).Points().Select(p => new PointF(p.X, p.Y))];

    /// <summary>The smallest upright pixel rectangle containing the box.</summary>
    public Rectangle MinAreaRect() => Common.CvInterop.ToRectangle(Common.CvInterop.ToCv(this).BoundingRect());
}
