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
    private readonly SemaphoreSlim _deviceScanLock = new(1, 1);
    private bool _disposed;

    public Naps2ScannerService()
    {
        _context = new ScanningContext(new ImageSharpImageContext());
        if (OperatingSystem.IsWindows())
        {
            string workerPath = Path.Combine(AppContext.BaseDirectory, "NAPS2.Worker.exe");
            if (File.Exists(workerPath))
            {
                try
                {
                    _context.SetUpWin32Worker();
                }
                catch
                {
                    // Fall back to native WIA / 64-bit scanning
                }
            }
        }
        _controller = new ScanController(_context);
    }

    public async Task<IReadOnlyList<ScannerDeviceInfo>> GetDevicesAsync(bool includeNetwork = false, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        cancellationToken.ThrowIfCancellationRequested();

        await _deviceScanLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var devices = await GetAllDevicesInternalAsync(includeNetwork, cancellationToken).ConfigureAwait(false);
            var result = new List<ScannerDeviceInfo>(devices.Count);

            foreach (var dev in devices)
            {
                if (dev == null) continue;
                string devId = dev.ID ?? string.Empty;
                string devName = string.IsNullOrWhiteSpace(dev.Name) ? (string.IsNullOrEmpty(devId) ? "Scanner" : devId) : dev.Name;
                result.Add(new ScannerDeviceInfo
                {
                    Id = $"{dev.Driver.ToString().ToUpperInvariant()}:{devId}",
                    Name = devName,
                    Driver = MapDriver(dev.Driver)
                });
            }

            return result.AsReadOnly();
        }
        finally
        {
            _deviceScanLock.Release();
        }
    }

    public async Task<IReadOnlyList<Mat>> ScanAsync(ScannerOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, this);

        cancellationToken.ThrowIfCancellationRequested();

        await _deviceScanLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
                        using var raw = new Mat();
                        CvInvoke.Imdecode(bytes, ImreadModes.AnyColor, raw);
                        var bgrMat = PhotoCropperEngine.NormalizeToBgr(raw);
                        mats.Add(bgrMat);
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
        finally
        {
            _deviceScanLock.Release();
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
                var twainDevices = await _controller.GetDeviceList(Driver.Twain)
                    .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                    .ConfigureAwait(false);
                allDevices.AddRange(twainDevices);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // TWAIN subsystem not present, timed out, or threw error
            }

            // 2. Enumerate WIA devices
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var wiaDevices = await _controller.GetDeviceList(Driver.Wia)
                    .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                    .ConfigureAwait(false);
                allDevices.AddRange(wiaDevices);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // WIA service not available, timed out, or threw error
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
                if (esclDevices != null)
                {
                    foreach (var escl in esclDevices)
                    {
                        if (escl != null && !string.IsNullOrEmpty(escl.ID) && !allDevices.Any(d => d != null && string.Equals(d.ID, escl.ID, StringComparison.OrdinalIgnoreCase)))
                        {
                            allDevices.Add(escl);
                        }
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

    public async Task<string?> GetDeviceBedDimensionsAsync(ScannerDeviceInfo device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ObjectDisposedException.ThrowIf(_disposed, this);

        cancellationToken.ThrowIfCancellationRequested();

        await _deviceScanLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool includeNetwork = device.Driver == ScannerDriverType.Escl ||
                                  device.Id.StartsWith("ESCL:", StringComparison.OrdinalIgnoreCase);
            var allDevices = await GetAllDevicesInternalAsync(includeNetwork, cancellationToken).ConfigureAwait(false);
            var selectedDevice = FindMatchingDevice(allDevices, device);
            if (selectedDevice == null) return null;

            var caps = await _controller.GetCaps(selectedDevice, cancellationToken).ConfigureAwait(false);
            var scanArea = caps?.FlatbedCaps?.PageSizeCaps?.ScanArea;
            if (scanArea != null && scanArea.Width > 0 && scanArea.Height > 0)
            {
                int widthMm = (int)Math.Round(scanArea.WidthInMm, MidpointRounding.AwayFromZero);
                int heightMm = (int)Math.Round(scanArea.HeightInMm, MidpointRounding.AwayFromZero);
                return $"{widthMm} × {heightMm} mm";
            }

            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            _deviceScanLock.Release();
        }
    }

    private static ScanDevice? FindMatchingDevice(IReadOnlyList<ScanDevice> allDevices, ScannerDeviceInfo? deviceInfo)
    {
        if (deviceInfo == null)
        {
            return allDevices.Count > 0 ? allDevices[0] : null;
        }

        var match = allDevices.FirstOrDefault(d =>
            string.Equals($"{d.Driver.ToString().ToUpperInvariant()}:{d.ID}", deviceInfo.Id, StringComparison.OrdinalIgnoreCase));

        match ??= allDevices.FirstOrDefault(d =>
            string.Equals(d.ID, deviceInfo.Id, StringComparison.OrdinalIgnoreCase) &&
            (deviceInfo.Driver == ScannerDriverType.Default || MapDriver(d.Driver) == deviceInfo.Driver));

        match ??= allDevices.FirstOrDefault(d =>
            string.Equals(d.Name, deviceInfo.Name, StringComparison.OrdinalIgnoreCase) &&
            (deviceInfo.Driver == ScannerDriverType.Default || MapDriver(d.Driver) == deviceInfo.Driver));

        match ??= allDevices.FirstOrDefault(d =>
            string.Equals(d.ID, deviceInfo.Id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(d.Name, deviceInfo.Name, StringComparison.OrdinalIgnoreCase));

        return match;
    }

    private async Task<ScanOptions> BuildScanOptionsAsync(ScannerOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool includeNetwork = options.Device?.Driver == ScannerDriverType.Escl ||
                              options.Device?.Id?.StartsWith("ESCL:", StringComparison.OrdinalIgnoreCase) == true;
        var allDevices = await GetAllDevicesInternalAsync(includeNetwork, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        ScanDevice? selectedDevice = FindMatchingDevice(allDevices, options.Device);
        if (selectedDevice == null)
        {
            if (options.Device != null)
            {
                throw new ScannerNotFoundException(
                    $"The selected scanner '{options.Device.Name}' was not found or is disconnected. Please check that the scanner is turned on and connected.");
            }

            throw new ScannerNotFoundException(
                "No scanner devices were detected on this system. Please verify that your scanner is turned on and connected.");
        }

        PageSize? targetPageSize = null;
        if (options.PageSize == ScannerPageSize.Auto)
        {
            try
            {
                var caps = await _controller.GetCaps(selectedDevice, cancellationToken).ConfigureAwait(false);
                var scanArea = caps?.FlatbedCaps?.PageSizeCaps?.ScanArea;
                if (scanArea != null && scanArea.Width > 0 && scanArea.Height > 0)
                {
                    targetPageSize = scanArea;
                }
            }
            catch
            {
                targetPageSize = PageSize.A4;
            }
        }
        else
        {
            targetPageSize = options.PageSize switch
            {
                ScannerPageSize.A4 => PageSize.A4,
                ScannerPageSize.Letter => PageSize.Letter,
                ScannerPageSize.Legal => PageSize.Legal,
                ScannerPageSize.B5 => new PageSize(182m, 257m, PageSizeUnit.Millimetre),
                ScannerPageSize.A5 => PageSize.A5,
                _ => null
            };
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
            Contrast = options.Contrast,
            PageSize = targetPageSize
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
            _disposed = true;
            _deviceScanLock.Dispose();
            _context.Dispose();
        }
    }
}
