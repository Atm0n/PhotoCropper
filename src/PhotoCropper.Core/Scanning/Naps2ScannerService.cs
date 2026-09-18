using Emgu.CV;
using Emgu.CV.CvEnum;
using NAPS2.Images;
using NAPS2.Images.ImageSharp;
using NAPS2.Scan;

namespace PhotoCropper.Core.Scanning;

/// <summary>
/// Cross-platform scanner service backed by NAPS2 SDK (supporting TWAIN, WIA, SANE, and eSCL).
/// </summary>
public sealed class Naps2ScannerService : IScannerService
{
    private readonly ScanningContext _context;
    private readonly ScanController _controller;
    private bool _disposed;

    public Naps2ScannerService()
    {
        _context = new ScanningContext(new ImageSharpImageContext());
        if (OperatingSystem.IsWindows())
        {
            _context.SetUpWin32Worker();
        }
        _controller = new ScanController(_context);
    }

    public async Task<IReadOnlyList<ScannerDeviceInfo>> GetDevicesAsync(bool includeNetwork = false, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        cancellationToken.ThrowIfCancellationRequested();

        var devices = await GetAllDevicesInternalAsync(includeNetwork, cancellationToken).ConfigureAwait(false);
        var result = new List<ScannerDeviceInfo>(devices.Count);

        foreach (var dev in devices)
        {
            result.Add(new ScannerDeviceInfo
            {
                Id = $"{dev.Driver.ToString().ToUpperInvariant()}:{dev.ID}",
                Name = dev.Name,
                Driver = MapDriver(dev.Driver)
            });
        }

        return result.AsReadOnly();
    }

    public async Task<IReadOnlyList<Mat>> ScanAsync(ScannerOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, this);

        cancellationToken.ThrowIfCancellationRequested();

        var napsOptions = await BuildScanOptionsAsync(options, cancellationToken).ConfigureAwait(false);
        var mats = new List<Mat>();

        try
        {
            await foreach (var image in _controller.Scan(napsOptions, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                using (image)
                using (var ms = new MemoryStream())
                {
                    image.Save(ms, ImageFileFormat.Png);
                    var bytes = ms.ToArray();
                    var mat = new Mat();
                    CvInvoke.Imdecode(bytes, ImreadModes.AnyColor, mat);
                    mats.Add(mat);
                }
            }

            return mats.AsReadOnly();
        }
        catch
        {
            foreach (var mat in mats)
            {
                mat.Dispose();
            }
            throw;
        }
    }

    public async Task<IReadOnlyList<string>> ScanToDirectoryAsync(
        ScannerOptions options,
        string outputDirectory,
        string fileNamePrefix = "scan_",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ObjectDisposedException.ThrowIf(_disposed, this);

        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(outputDirectory);

        var napsOptions = await BuildScanOptionsAsync(options, cancellationToken).ConfigureAwait(false);
        var savedPaths = new List<string>();

        int index = 1;
        await foreach (var image in _controller.Scan(napsOptions, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            using (image)
            {
                string filePath;
                do
                {
                    filePath = Path.Combine(outputDirectory, $"{fileNamePrefix}{index:D4}.png");
                    index++;
                } while (File.Exists(filePath));

                using (var fs = File.Create(filePath))
                {
                    image.Save(fs, ImageFileFormat.Png);
                }

                savedPaths.Add(filePath);
            }
        }

        return savedPaths.AsReadOnly();
    }

    private async Task<IReadOnlyList<ScanDevice>> GetAllDevicesInternalAsync(bool includeNetwork = false, CancellationToken cancellationToken = default)
    {
        var allDevices = new List<ScanDevice>();

        if (OperatingSystem.IsWindows())
        {
            // 1. Enumerate TWAIN devices (32-bit supported out-of-process via Win32 worker)
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var twainDevices = await _controller.GetDeviceList(Driver.Twain).ConfigureAwait(false);
                allDevices.AddRange(twainDevices);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // TWAIN subsystem not present or threw error
            }

            // 2. Enumerate WIA devices
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var wiaDevices = await _controller.GetDeviceList(Driver.Wia).ConfigureAwait(false);
                allDevices.AddRange(wiaDevices);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // WIA service not available
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            // SANE on Linux
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var saneDevices = await _controller.GetDeviceList(Driver.Sane).ConfigureAwait(false);
                allDevices.AddRange(saneDevices);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
        }
        else
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var defaultDevices = await _controller.GetDeviceList().ConfigureAwait(false);
                allDevices.AddRange(defaultDevices);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
        }

        // Only probe eSCL (Apple AirScan / Mopria network scanners) when explicitly requested
        if (includeNetwork)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var esclDevices = await _controller.GetDeviceList(Driver.Escl).ConfigureAwait(false);
                foreach (var escl in esclDevices)
                {
                    if (!allDevices.Any(d => string.Equals(d.ID, escl.ID, StringComparison.OrdinalIgnoreCase)))
                    {
                        allDevices.Add(escl);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
        }

        return allDevices.AsReadOnly();
    }

    private async Task<ScanOptions> BuildScanOptionsAsync(ScannerOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool includeNetwork = options.Device?.Driver == ScannerDriverType.Escl ||
                              options.Device?.Id?.StartsWith("ESCL:", StringComparison.OrdinalIgnoreCase) == true;
        var allDevices = await GetAllDevicesInternalAsync(includeNetwork, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        ScanDevice? selectedDevice = null;

        if (options.Device != null)
        {
            selectedDevice = allDevices.FirstOrDefault(d =>
                string.Equals($"{d.Driver.ToString().ToUpperInvariant()}:{d.ID}", options.Device.Id, StringComparison.OrdinalIgnoreCase));

            selectedDevice ??= allDevices.FirstOrDefault(d =>
                string.Equals(d.ID, options.Device.Id, StringComparison.OrdinalIgnoreCase) &&
                (options.Device.Driver == ScannerDriverType.Default || MapDriver(d.Driver) == options.Device.Driver));

            selectedDevice ??= allDevices.FirstOrDefault(d =>
                string.Equals(d.Name, options.Device.Name, StringComparison.OrdinalIgnoreCase) &&
                (options.Device.Driver == ScannerDriverType.Default || MapDriver(d.Driver) == options.Device.Driver));

            selectedDevice ??= allDevices.FirstOrDefault(d =>
                string.Equals(d.ID, options.Device.Id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.Name, options.Device.Name, StringComparison.OrdinalIgnoreCase));

            if (selectedDevice == null)
            {
                throw new ScannerNotFoundException(
                    $"The selected scanner '{options.Device.Name}' was not found or is disconnected. Please check that the scanner is turned on and connected.");
            }
        }
        else
        {
            if (allDevices.Count == 0)
            {
                throw new ScannerNotFoundException(
                    "No scanner devices were detected on this system. Please verify that your scanner is turned on and connected.");
            }

            selectedDevice = allDevices[0];
        }

        var scanOptions = new ScanOptions
        {
            Device = selectedDevice,
            Dpi = options.Dpi,
            BitDepth = options.ColorMode switch
            {
                ScannerColorMode.Grayscale => BitDepth.Grayscale,
                ScannerColorMode.BlackAndWhite => BitDepth.BlackAndWhite,
                _ => BitDepth.Color
            },
            Brightness = options.Brightness,
            Contrast = options.Contrast
        };

        return scanOptions;
    }

    private static ScannerDriverType MapDriver(Driver driver) => driver switch
    {
        Driver.Twain => ScannerDriverType.Twain,
        Driver.Wia => ScannerDriverType.Wia,
        Driver.Sane => ScannerDriverType.Sane,
        Driver.Escl => ScannerDriverType.Escl,
        _ => ScannerDriverType.Default
    };

    public void Dispose()
    {
        if (!_disposed)
        {
            _context.Dispose();
            _disposed = true;
        }
    }
}
