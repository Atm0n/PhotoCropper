using Emgu.CV;

namespace PhotoCropper.Core.Scanning;

/// <summary>
/// Abstraction for enumerating scanner devices and capturing images.
/// </summary>
public interface IScannerService : IDisposable
{
    /// <summary>
    /// Enumerates all available scanners across supported drivers (TWAIN, WIA, SANE, and optionally network eSCL).
    /// </summary>
    Task<IReadOnlyList<ScannerDeviceInfo>> GetDevicesAsync(bool includeNetwork = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a scan using the specified options and returns decoded BGR OpenCV Mats.
    /// </summary>
    Task<IReadOnlyList<Mat>> ScanAsync(ScannerOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a scan and writes raw image files directly to the target directory.
    /// </summary>
    Task<IReadOnlyList<string>> ScanToDirectoryAsync(
        ScannerOptions options,
        string outputDirectory,
        string fileNamePrefix = "scan_",
        CancellationToken cancellationToken = default);
}
