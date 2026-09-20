using Emgu.CV.Structure;

namespace PhotoCropper.Core.Models;

public record DetectionOptions
{
    public double BackgroundTolerance { get; set; } = 30;
    public double MinAreaFactor { get; set; } = 0.01; // 1% of scan
    public double MaxAreaFactor { get; set; } = 0.90; // 90% of scan
    public double CannyLowThreshold { get; set; } = 20;
    public double CannyHighThreshold { get; set; } = 50;
    public MCvScalar? CustomBackgroundColorHsv { get; set; }
    public bool AutoOrientPhotos { get; set; } = true;
    public bool RestoreVintageColors { get; set; } = true;
    public bool RemoveDustAndScratches { get; set; } = true;
    public string BoundingBoxColor { get; set; } = "Red";

    public MCvScalar GetBoundingBoxColorBgr() => BoundingBoxColor?.ToUpperInvariant() switch
    {
        "AMBER" or "ORANGE" => new MCvScalar(0, 165, 255),
        "CYAN" or "BLUE" => new MCvScalar(255, 255, 0),
        "MAGENTA" => new MCvScalar(255, 0, 255),
        "LIME" => new MCvScalar(0, 255, 0),
        _ => new MCvScalar(0, 0, 255)
    };
}
