using Emgu.CV.Structure;
using PhotoCropper.Core.Common;

namespace PhotoCropper.Core.Models;

public record DetectionOptions
{
    public double BackgroundTolerance { get; set; } = AppConstants.DefaultBackgroundTolerance;
    public double MinAreaFactor { get; set; } = AppConstants.DefaultMinAreaFactor;
    public double MaxAreaFactor { get; set; } = AppConstants.DefaultMaxAreaFactor;
    public double CannyLowThreshold { get; set; } = AppConstants.DefaultCannyLow;
    public double CannyHighThreshold { get; set; } = AppConstants.DefaultCannyHigh;
    public MCvScalar? CustomBackgroundColorHsv { get; set; }
    public bool AutoOrientPhotos { get; set; } = true;
    public bool RestoreVintageColors { get; set; } = true;
    public bool RemoveDustAndScratches { get; set; } = true;
    public string BoundingBoxColor { get; set; } = AppConstants.DefaultBoundingBoxColor;

    /// <summary>
    /// Converts a user-facing sensitivity percentage (0% to 100%) to internal background match tolerance.
    /// Higher sensitivity means higher eagerness to detect photos (tighter background subtraction, retaining light regions).
    /// </summary>
    public static double SensitivityToTolerance(double sensitivity)
    {
        double clamped = Math.Clamp(sensitivity, 0.0, 100.0);
        double t = (100.0 - clamped) / 100.0;
        return Math.Round(4.0 + Math.Pow(t, 1.4) * 41.0, 1);
    }

    /// <summary>
    /// Converts internal background match tolerance to user-facing sensitivity percentage (0% to 100%).
    /// </summary>
    public static double ToleranceToSensitivity(double tolerance)
    {
        double clamped = Math.Clamp(tolerance, 4.0, 45.0);
        double t = Math.Clamp((clamped - 4.0) / 41.0, 0.0, 1.0);
        return Math.Round(100.0 - Math.Pow(t, 1.0 / 1.4) * 100.0);
    }

    public MCvScalar GetBoundingBoxColorBgr()
    {
        if (!string.IsNullOrWhiteSpace(BoundingBoxColor) && BoundingBoxColor.StartsWith('#'))
        {
            string hex = BoundingBoxColor.TrimStart('#');
            if (hex.Length == 6 &&
                byte.TryParse(hex.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
                byte.TryParse(hex.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
                byte.TryParse(hex.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
            {
                return new MCvScalar(b, g, r);
            }
            if (hex.Length == 8 &&
                byte.TryParse(hex.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte ar) &&
                byte.TryParse(hex.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte ag) &&
                byte.TryParse(hex.AsSpan(6, 2), System.Globalization.NumberStyles.HexNumber, null, out byte ab))
            {
                return new MCvScalar(ab, ag, ar);
            }
        }

        return BoundingBoxColor?.ToUpperInvariant() switch
        {
            "AMBER" or "ORANGE" => new MCvScalar(0, 165, 255),
            "CYAN" or "BLUE" => new MCvScalar(255, 255, 0),
            "MAGENTA" => new MCvScalar(255, 0, 255),
            "LIME" => new MCvScalar(0, 255, 0),
            _ => new MCvScalar(0, 0, 255)
        };
    }
}
