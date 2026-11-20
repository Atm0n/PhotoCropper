using OpenCvSharp;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Extraction;
using PhotoCropper.Core.Models;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;

namespace PhotoCropper.Core.Detection;

/// <summary>Geometry shared by the engine's box edits: right-angle rotations and mapping between a photo and its box.</summary>
internal static class CropGeometry
{
    /// <summary>Whether every corner of <paramref name="box"/> lies on <paramref name="image"/>.</summary>
    public static bool IsInside(RotatedBox box, Mat image) =>
        box.GetVertices().All(p => p.X >= 0 && p.Y >= 0 && p.X <= image.Width - 1 && p.Y <= image.Height - 1);

    /// <summary>An angle in degrees as 0, 90, 180 or 270 (rounded down to a right angle, negatives wrapped).</summary>
    public static int NormalizeRightAngle(int degrees) => ((degrees % 360) + 360) % 360 / 90 * 90;

    /// <summary>The OpenCV rotation for a clockwise right angle (90, 180 or 270).</summary>
    public static RotateFlags ToRotateFlags(int clockwiseDegrees) => clockwiseDegrees switch
    {
        90 => RotateFlags.Rotate90Clockwise,
        180 => RotateFlags.Rotate180,
        _ => RotateFlags.Rotate90Counterclockwise
    };

    /// <summary>The candidate moved to <paramref name="box"/>, with its outline, bounding rectangle and area to match.</summary>
    public static CropCandidate WithBox(this CropCandidate candidate, RotatedBox box)
    {
        var pts = box.GetVertices().Select(DrawingPoint.Round).ToArray();
        return candidate with
        {
            Rotated = box,
            ShapePoints = pts,
            Rect = CvInterop.ToRectangle(Cv2.BoundingRect(CvInterop.ToCv(pts))),
            Area = box.Size.Width * box.Size.Height
        };
    }

    /// <summary>
    /// Maps a crop rectangle in an extracted photo back to a box on the scan.
    /// </summary>
    /// <param name="box">The box the photo was extracted from.</param>
    /// <param name="clockwiseRotation">Total rotation (auto-orientation + user) applied to the extracted photo.</param>
    /// <param name="photoSize">Size of the photo as currently shown, i.e. after that rotation.</param>
    /// <param name="crop">The crop, in the coordinates of the photo as currently shown.</param>
    public static RotatedBox MapPhotoCropToScan(RotatedBox box, int clockwiseRotation, DrawingSize photoSize, DrawingRectangle crop)
    {
        // The extraction warps the box's TL/TR/BL corners to the photo's (0,0)/(W,0)/(0,H) before rotating.
        Point2f[] p = PhotoExtractionEngine.OrderBoxPoints(CvInterop.ToCv(box).Points());
        int rotation = NormalizeRightAngle(clockwiseRotation);
        bool swapped = rotation is 90 or 270;
        float w = swapped ? photoSize.Height : photoSize.Width;
        float h = swapped ? photoSize.Width : photoSize.Height;

        // Undo the rotation: shown-photo point (x, y) -> unrotated extraction point (u, v).
        (float U, float V) Unrotate(float x, float y) => rotation switch
        {
            90 => (y, h - x),
            180 => (w - x, h - y),
            270 => (w - y, x),
            _ => (x, y)
        };

        var (ua, va) = Unrotate(crop.Left, crop.Top);
        var (ub, vb) = Unrotate(crop.Right, crop.Bottom);
        float u0 = Math.Min(ua, ub), u1 = Math.Max(ua, ub);
        float v0 = Math.Min(va, vb), v1 = Math.Max(va, vb);

        Point2f ToScan(float u, float v) => new(
            p[0].X + u / w * (p[1].X - p[0].X) + v / h * (p[3].X - p[0].X),
            p[0].Y + u / w * (p[1].Y - p[0].Y) + v / h * (p[3].Y - p[0].Y));

        return CvInterop.ToBox(Cv2.MinAreaRect([ToScan(u0, v0), ToScan(u1, v0), ToScan(u1, v1), ToScan(u0, v1)]));
    }
}
