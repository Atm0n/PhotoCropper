using Emgu.CV.Structure;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;

namespace PhotoCropper.Core.Models;

public readonly record struct CropCandidate
{
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Performance-critical polygon coordinate buffer")]
    public Point[] ShapePoints { get; init; }
    public Rectangle Rect { get; init; }
    public double Score { get; init; }
    public RotatedRect Rotated { get; init; }
    public double Area { get; init; }
    public double Rectangularity { get; init; }
    public double Convexity { get; init; }

    public CropCandidate(
        Point[] shapePoints,
        Rectangle rect,
        double score,
        RotatedRect rotated,
        double area,
        double rectangularity,
        double convexity)
    {
        ShapePoints = shapePoints;
        Rect = rect;
        Score = score;
        Rotated = rotated;
        Area = area;
        Rectangularity = rectangularity;
        Convexity = convexity;
    }

    public PointF GetRotationHandlePoint(float offsetDistance = 50f, Rectangle? imageBounds = null)
    {
        PointF[] ordered = PhotoCropper.Core.Extraction.PhotoExtractionEngine.OrderBoxPoints(Rotated.GetVertices());
        PointF topMid = new((ordered[0].X + ordered[1].X) / 2f, (ordered[0].Y + ordered[1].Y) / 2f);

        // Direction vector from center pointing toward topMid
        float vx = topMid.X - Rotated.Center.X;
        float vy = topMid.Y - Rotated.Center.Y;
        float len = (float)Math.Sqrt(vx * vx + vy * vy);

        float ux;
        float uy;
        if (len < 1e-4f)
        {
            ux = 0f;
            uy = -1f;
        }
        else
        {
            ux = vx / len;
            uy = vy / len;
        }

        PointF handle = new(topMid.X + ux * offsetDistance, topMid.Y + uy * offsetDistance);

        if (imageBounds.HasValue)
        {
            var bounds = imageBounds.Value;
            const float padding = 50f;
            float minX = bounds.Left + padding;
            float maxX = bounds.Right - padding;
            float minY = bounds.Top + padding;
            float maxY = bounds.Bottom - padding;

            if (minX <= maxX && minY <= maxY)
            {
                handle = new PointF(
                    Math.Clamp(handle.X, minX, maxX),
                    Math.Clamp(handle.Y, minY, maxY));
            }
        }

        return handle;
    }
}
