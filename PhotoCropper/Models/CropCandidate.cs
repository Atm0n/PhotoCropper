using Emgu.CV.Structure;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;

namespace PhotoCropper.Models;

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
}
