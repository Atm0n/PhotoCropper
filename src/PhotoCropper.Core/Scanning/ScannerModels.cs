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
/// Preset bed or page size dimensions for scanner acquisition.
/// </summary>
public enum ScannerPageSize
{
    Auto = 0,
    A4 = 1,
    Letter = 2,
    Legal = 3,
    B5 = 4,
    A5 = 5
}

/// <summary>
/// Represents a hardware scanner detected on the host system.
/// </summary>
public sealed class ScannerDeviceInfo
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public ScannerDriverType Driver { get; init; } = ScannerDriverType.Default;

    public override string ToString() => $"{(string.IsNullOrWhiteSpace(Name) ? "Unknown Scanner" : Name)} ({Driver})";
}

/// <summary>
/// User-configurable options for acquiring scans.
/// </summary>
public sealed class ScannerOptions
{
    public ScannerDeviceInfo? Device { get; set; }

    public int Dpi { get; set; } = 300;

    public ScannerColorMode ColorMode { get; set; } = ScannerColorMode.Color;

    public ScannerPageSize PageSize { get; set; } = ScannerPageSize.Auto;

    public int Brightness { get; set; }

    public int Contrast { get; set; }
}

/// <summary>
/// Helper extensions for parsing and formatting <see cref="ScannerPageSize"/>.
/// </summary>
public static class ScannerPageSizeExtensions
{
    public static ScannerPageSize ToScannerPageSize(string? value) => value?.ToUpperInvariant() switch
    {
        "A4" => ScannerPageSize.A4,
        "LETTER" or "LTR" => ScannerPageSize.Letter,
        "LEGAL" => ScannerPageSize.Legal,
        "B5" => ScannerPageSize.B5,
        "A5" => ScannerPageSize.A5,
        "AUTO" or "MAX" or "DEFAULT" => ScannerPageSize.Auto,
        _ => ScannerPageSize.Auto
    };

    public static string ToSettingValue(this ScannerPageSize size) => size switch
    {
        ScannerPageSize.A4 => "A4",
        ScannerPageSize.Letter => "Letter",
        ScannerPageSize.Legal => "Legal",
        ScannerPageSize.B5 => "B5",
        ScannerPageSize.A5 => "A5",
        _ => "Auto"
    };
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

