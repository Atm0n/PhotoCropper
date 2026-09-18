using PhotoCropper.Core.Scanning;
using Shouldly;
using Xunit;

namespace PhotoCropper.Core.Tests.Scanning;

public sealed class ScannerServiceTests
{
    [Fact]
    public void ScannerModels_DefaultValues_AreExpected()
    {
        var options = new ScannerOptions();
        options.Dpi.ShouldBe(300);
        options.ColorMode.ShouldBe(ScannerColorMode.Color);
        options.Brightness.ShouldBe(0);
        options.Contrast.ShouldBe(0);
        options.Device.ShouldBeNull();
    }

    [Fact]
    public void ScannerDeviceInfo_ToString_FormatsCorrectly()
    {
        var device = new ScannerDeviceInfo
        {
            Id = "twain:Canon LiDE 400",
            Name = "Canon LiDE 400",
            Driver = ScannerDriverType.Twain
        };

        device.ToString().ShouldBe("Canon LiDE 400 (Twain)");
    }

    [Fact]
    public async Task Naps2ScannerService_LifecycleAndDeviceEnumeration_CompletesWithoutError()
    {
        using var service = new Naps2ScannerService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // Enumerating devices should succeed across platforms even if no physical scanner is connected
        var devices = await service.GetDevicesAsync(includeNetwork: false, cancellationToken: cts.Token);
        devices.ShouldNotBeNull();
    }

    [Fact]
    public async Task Naps2ScannerService_DisposedInstance_ThrowsObjectDisposedException()
    {
        var service = new Naps2ScannerService();
        service.Dispose();

        await Should.ThrowAsync<ObjectDisposedException>(async () =>
        {
            await service.GetDevicesAsync();
        });

        await Should.ThrowAsync<ObjectDisposedException>(async () =>
        {
            await service.ScanAsync(new ScannerOptions());
        });
    }

    [Fact]
    public async Task Naps2ScannerService_ScanAsync_WithNonExistentDevice_ThrowsScannerNotFoundException()
    {
        using var service = new Naps2ScannerService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var options = new ScannerOptions
        {
            Device = new ScannerDeviceInfo
            {
                Id = "virtual:non_existent_scanner_id_9999",
                Name = "NonExistent Scanner 9999",
                Driver = ScannerDriverType.Twain
            }
        };

        var ex = await Should.ThrowAsync<ScannerNotFoundException>(async () =>
        {
            await service.ScanAsync(options, cts.Token);
        });

        ex.Message.ShouldContain("NonExistent Scanner 9999");
    }

    [Fact]
    public async Task Naps2ScannerService_ScanToDirectoryAsync_WithNonExistentDevice_ThrowsScannerNotFoundException()
    {
        using var service = new Naps2ScannerService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var tempDir = Path.Combine(Path.GetTempPath(), "pc_scanner_test_" + Guid.NewGuid().ToString("N"));

        try
        {
            var options = new ScannerOptions
            {
                Device = new ScannerDeviceInfo
                {
                    Id = "virtual:non_existent_scanner_id_8888",
                    Name = "NonExistent Scanner 8888",
                    Driver = ScannerDriverType.Wia
                }
            };

            var ex = await Should.ThrowAsync<ScannerNotFoundException>(async () =>
            {
                await service.ScanToDirectoryAsync(options, tempDir, cancellationToken: cts.Token);
            });

            ex.Message.ShouldContain("NonExistent Scanner 8888");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task Naps2ScannerService_ScanAsync_WhenPreCancelled_ThrowsOperationCanceledException()
    {
        using var service = new Naps2ScannerService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var options = new ScannerOptions
        {
            Device = new ScannerDeviceInfo
            {
                Id = "test:device",
                Name = "Test Device"
            }
        };

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await service.ScanAsync(options, cts.Token);
        });
    }
}

