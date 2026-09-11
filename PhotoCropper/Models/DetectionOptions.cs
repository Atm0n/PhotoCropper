using Emgu.CV.Structure;

namespace PhotoCropper.Models;

public record DetectionOptions
{
    public double BackgroundTolerance { get; set; } = 30;
    public double MinAreaFactor { get; set; } = 0.01; // 1% of scan
    public double MaxAreaFactor { get; set; } = 0.90; // 90% of scan
    public double CannyLowThreshold { get; set; } = 20;
    public double CannyHighThreshold { get; set; } = 50;
    public MCvScalar? CustomBackgroundColorHsv { get; set; }
    public bool AutoOrientPhotos { get; set; }
}
