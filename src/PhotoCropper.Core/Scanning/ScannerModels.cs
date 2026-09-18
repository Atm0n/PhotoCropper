namespace PhotoCropper.Core.Scanning;

/// <summary>
/// Driver type used to communicate with the scanning hardware.
/// </summary>
public enum ScannerDriverType
{
    Default = 0,
    Twain = 1,
    Wia = 2,
    Sane = 3,
    Escl = 4
}

/// <summary>
/// Color depth mode for the scanner acquisition.
/// </summary>
public enum ScannerColorMode
{
    Color = 0,
    Grayscale = 1,
    BlackAndWhite = 2
}

/// <summary>
/// Represents a hardware scanner detected on the host system.
/// </summary>
public sealed class ScannerDeviceInfo
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public ScannerDriverType Driver { get; init; } = ScannerDriverType.Default;

    public override string ToString() => $"{Name} ({Driver})";
}

/// <summary>
/// User-configurable options for acquiring scans.
/// </summary>
public sealed class ScannerOptions
{
    public ScannerDeviceInfo? Device { get; set; }

    public int Dpi { get; set; } = 300;

    public ScannerColorMode ColorMode { get; set; } = ScannerColorMode.Color;

    public int Brightness { get; set; }

    public int Contrast { get; set; }
}

/// <summary>
/// Exception thrown when a requested or default scanner device is not found or is disconnected.
/// </summary>
public class ScannerNotFoundException : InvalidOperationException
{
    public ScannerNotFoundException() : base("Scanner device not found or is disconnected.") { }

    public ScannerNotFoundException(string message) : base(message) { }

    public ScannerNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}

