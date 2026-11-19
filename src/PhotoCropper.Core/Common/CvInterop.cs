using PhotoCropper.Core.Models;
using CvPoint = OpenCvSharp.Point;
using CvPoint2f = OpenCvSharp.Point2f;
using CvRect = OpenCvSharp.Rect;
using CvRotatedRect = OpenCvSharp.RotatedRect;
using CvScalar = OpenCvSharp.Scalar;
using CvSize = OpenCvSharp.Size;
using CvSize2f = OpenCvSharp.Size2f;
using DrawingPoint = System.Drawing.Point;
using DrawingPointF = System.Drawing.PointF;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;
using DrawingSizeF = System.Drawing.SizeF;

namespace PhotoCropper.Core.Common;

/// <summary>
/// Conversions between the app's own geometry (System.Drawing types, <see cref="RotatedBox"/>, <see cref="ColorScalar"/>)
/// and OpenCvSharp's. The models and the GUI keep System.Drawing types; OpenCV code converts at its boundary.
/// </summary>
public static class CvInterop
{
    public static CvRect ToCv(DrawingRectangle r) => new(r.X, r.Y, r.Width, r.Height);
    public static DrawingRectangle ToRectangle(CvRect r) => new(r.X, r.Y, r.Width, r.Height);

    public static CvPoint ToCv(DrawingPoint p) => new(p.X, p.Y);
    public static DrawingPoint ToPoint(CvPoint p) => new(p.X, p.Y);

    public static CvPoint2f ToCv(DrawingPointF p) => new(p.X, p.Y);
    public static DrawingPointF ToPointF(CvPoint2f p) => new(p.X, p.Y);

    public static CvSize ToCv(DrawingSize s) => new(s.Width, s.Height);
    public static DrawingSize ToSize(CvSize s) => new(s.Width, s.Height);

    public static CvSize2f ToCv(DrawingSizeF s) => new(s.Width, s.Height);

    public static CvRotatedRect ToCv(RotatedBox box) => new(ToCv(box.Center), ToCv(box.Size), box.Angle);
    public static RotatedBox ToBox(CvRotatedRect r) => new(new DrawingPointF(r.Center.X, r.Center.Y), new DrawingSizeF(r.Size.Width, r.Size.Height), r.Angle);

    public static CvScalar ToCv(ColorScalar c) => new(c.V0, c.V1, c.V2, c.V3);
    public static ColorScalar ToColor(CvScalar s) => new(s.Val0, s.Val1, s.Val2, s.Val3);

    public static CvPoint[] ToCv(IReadOnlyList<DrawingPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var result = new CvPoint[points.Count];
        for (int i = 0; i < result.Length; i++) result[i] = ToCv(points[i]);
        return result;
    }

    public static DrawingPoint[] ToPoints(IReadOnlyList<CvPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var result = new DrawingPoint[points.Count];
        for (int i = 0; i < result.Length; i++) result[i] = ToPoint(points[i]);
        return result;
    }

    public static CvPoint2f[] ToCv(IReadOnlyList<DrawingPointF> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var result = new CvPoint2f[points.Count];
        for (int i = 0; i < result.Length; i++) result[i] = ToCv(points[i]);
        return result;
    }
}
