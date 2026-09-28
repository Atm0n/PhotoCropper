namespace PhotoCropper.Core.Common;

public static class AppConstants
{
    public const string FormatJpeg = "JPEG";
    public const string FormatPng = "PNG";

    public const string ExtensionJpg = ".jpg";
    public const string ExtensionJpeg = ".jpeg";
    public const string ExtensionPng = ".png";

    public const string ColorRed = "Red";
    public const string ColorAmber = "Amber";
    public const string ColorCyan = "Cyan";
    public const string ColorMagenta = "Magenta";
    public const string ColorLime = "Lime";

    public const string DefaultImageFormat = FormatJpeg;
    public const string PngImageFormat = FormatPng;
    public const string DefaultNamingPattern = "{original}_{index}";
    public const string DefaultBoundingBoxColor = ColorRed;
    public const string DefaultLanguage = "en-US";
    public const string DefaultTheme = "Dark";
    public const double DefaultSensitivity = 50.0;
    public const double DefaultBackgroundTolerance = 30.0;
    public const double DefaultCannyLow = 20.0;
    public const double DefaultCannyHigh = 50.0;
    public const double DefaultMinAreaFactor = 0.01;
    public const double DefaultMaxAreaFactor = 0.90;
    public const double LowCoverageThreshold = 0.50;
    public const int DefaultScannerDpi = 300;
    public const string DefaultScannerPageSize = "Auto";
    public const int DefaultJpegQuality = 100;
    public const int DefaultLookaheadAhead = 4;
    public const int DefaultLookaheadBehind = 4;

    /// <summary>
    /// Multiplier applied to the low Canny threshold to derive the high threshold (Canny ratio).
    /// This value is standardized here to avoid silent divergence across the pipeline.
    /// </summary>
    public const double DefaultCannyHighRatio = 2.5;

    /// <summary>
    /// Milliseconds to wait between file-write retries when a transient I/O lock is detected.
    /// </summary>
    public const int ExportFileRetryDelayMs = 30;

    /// <summary>
    /// Maximum number of unique filename collision retries before throwing an IOException.
    /// Guards against an infinite loop on a full or locked disk.
    /// </summary>
    public const int MaxExportPathRetries = 10_000;
}
