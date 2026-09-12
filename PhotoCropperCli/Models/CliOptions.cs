namespace PhotoCropperCli.Models;

internal sealed class CliOptions
{
    public List<string> Inputs { get; } = [];
    public string? OutputDirectory { get; set; }
    public string Format { get; set; } = "JPEG";
    public int JpegQuality { get; set; } = 90;
    public double Tolerance { get; set; } = 25.0;
    public double MinAreaFactor { get; set; } = 0.15;
    public double MaxAreaFactor { get; set; } = 0.90;
    public double CannyLow { get; set; } = 20.0;
    public bool AutoOrient { get; set; } = true;
    public bool RestoreColors { get; set; } = true;
    public bool RemoveDust { get; set; } = true;
    public int Threads { get; set; } = Math.Max(1, Environment.ProcessorCount);
    public bool Recursive { get; set; }
    public bool Verbose { get; set; }
}
