namespace PhotoCropper.Cli.Models;

internal sealed class CliOptions
{
    public List<string> Inputs { get; } = [];
    public string? OutputDirectory { get; set; }
    public string Format { get; set; } = "JPEG";
    public int JpegQuality { get; set; } = 100;
    public double Tolerance { get; set; } = 25.0;
    public double MinAreaFactor { get; set; } = 0.25;
    public double MaxAreaFactor { get; set; } = 0.90;
    public double CannyLow { get; set; } = 20.0;
    public bool AutoOrient { get; set; } = true;
    public bool RestoreColors { get; set; } = true;
    public bool RemoveDust { get; set; } = true;
    public int Threads { get; set; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
    public bool Recursive { get; set; }
    public bool Verbose { get; set; }
    public bool AutoTune { get; set; }
    public bool AutoAdjustLowCoverage { get; set; }
    public double MinCoverageThresholdPercent { get; set; } = PhotoCropper.Core.Common.AppConstants.LowCoverageThreshold * 100.0;
    public string? CopyUndetectedDirectory { get; set; }
    public bool NonInteractive { get; set; }
    public bool Interactive { get; set; }
    public int MinExpectedPhotos { get; set; } = 1;
    public int MaxExpectedPhotos { get; set; } = int.MaxValue;
    public string FileNamePattern { get; set; } = "{original}_{index}";
    public int? Year { get; set; }
    public string? Date { get; set; }
    public string? Description { get; set; }
}
