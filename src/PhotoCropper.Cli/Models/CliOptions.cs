namespace PhotoCropper.Cli.Models;

/// <summary>Which part of the RawScans → Cropped → Final pipeline the CLI runs.</summary>
internal enum PipelineStage
{
    /// <summary>Detect, crop, restore and export in one go (the default).</summary>
    All,

    /// <summary>Detect and crop only: lossless PNGs with geometry fixes but no restoration.</summary>
    Crop,

    /// <summary>Restore existing photo files (no detection), e.g. photos already cropped.</summary>
    Restore
}

internal sealed class CliOptions
{
    public PipelineStage Stage { get; set; } = PipelineStage.All;

    /// <summary>True when -p/--pattern was given; the restore stage keeps file names otherwise.</summary>
    public bool FileNamePatternSpecified { get; set; }

    public List<string> Inputs { get; } = [];

    /// <summary>--project: a project folder to run the app's RawScans → Cropped → Final pipeline on.</summary>
    public string? ProjectDirectory { get; set; }
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
    public bool AlwaysAutoTune { get; set; }
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
    public string? Photographer { get; set; }
}
