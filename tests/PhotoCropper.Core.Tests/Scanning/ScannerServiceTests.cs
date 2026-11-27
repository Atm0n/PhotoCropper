using PhotoCropper.Core.Scanning;

namespace PhotoCropper.Core.Tests.Scanning;

[Collection("ScannerServiceTests")]
public sealed class ScannerServiceTests
{
    [Fact]
    public void ScannerModelsDefaultValuesAreExpected()
    {
        var options = new ScannerOptions();
        options.Dpi.ShouldBe(300);
        options.ColorMode.ShouldBe(ScannerColorMode.Color);
        options.PageSize.ShouldBe(ScannerPageSize.Auto);
        options.Brightness.ShouldBe(0);
        options.Contrast.ShouldBe(0);
        options.Device.ShouldBeNull();
    }

    [Theory]
    [InlineData("A4", ScannerPageSize.A4, "A4")]
    [InlineData("a4", ScannerPageSize.A4, "A4")]
    [InlineData("Letter", ScannerPageSize.Letter, "Letter")]
    [InlineData("LTR", ScannerPageSize.Letter, "Letter")]
    [InlineData("Legal", ScannerPageSize.Legal, "Legal")]
    [InlineData("B5", ScannerPageSize.B5, "B5")]
    [InlineData("A5", ScannerPageSize.A5, "A5")]
    [InlineData("Auto", ScannerPageSize.Auto, "Auto")]
    [InlineData("Max", ScannerPageSize.Auto, "Auto")]
    [InlineData("Default", ScannerPageSize.Auto, "Auto")]
    [InlineData("UnknownValue", ScannerPageSize.Auto, "Auto")]
    [InlineData(null, ScannerPageSize.Auto, "Auto")]
    public void ScannerPageSizeExtensionsConversionsAreExpected(string? input, ScannerPageSize expectedEnum, string expectedSetting)
    {
        var parsed = ScannerPageSizeExtensions.ToScannerPageSize(input);
        parsed.ShouldBe(expectedEnum);
        parsed.ToSettingValue().ShouldBe(expectedSetting);
    }

    [Fact]
    public void ScannerDeviceInfoToStringFormatsCorrectly()
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
    public async Task Naps2ScannerServiceLifecycleAndDeviceEnumerationCompletesWithoutError()
    {
        using var service = new Naps2ScannerService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // Enumerating devices should succeed across platforms even if no physical scanner is connected
        var devices = await service.GetDevicesAsync(includeNetwork: false, cancellationToken: cts.Token);
        devices.ShouldNotBeNull();
    }

    /// <summary>
    /// On Linux, every scanner SANE knows must be listed. SANE's "test" backend provides two virtual scanners
    /// (test:0 and test:1); the Linux CI job turns it on, elsewhere this test is skipped.
    /// </summary>
    [Fact]
    public async Task Naps2ScannerServiceOnLinuxListsSanesTestScanners()
    {
        const string saneBackends = "/etc/sane.d/dll.conf";
        bool testBackendOn = OperatingSystem.IsLinux() && File.Exists(saneBackends) &&
            (await File.ReadAllLinesAsync(saneBackends, TestContext.Current.CancellationToken)).Any(l => l.Trim() == "test");
        Assert.SkipUnless(testBackendOn, "Needs Linux with SANE's test backend turned on in /etc/sane.d/dll.conf.");

        using var service = new Naps2ScannerService();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(60));

        var devices = await service.GetDevicesAsync(includeNetwork: false, cancellationToken: cts.Token);

        devices.Select(d => d.Id).ShouldContain("SANE:test:0");
        devices.ShouldAllBe(d => d.Driver == ScannerDriverType.Sane);
    }

    [Fact]
    public async Task Naps2ScannerServiceDisposedInstanceThrowsObjectDisposedException()
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
    public async Task Naps2ScannerServiceScanAsyncWithNonExistentDeviceThrowsScannerNotFoundException()
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
    public async Task Naps2ScannerServiceScanToDirectoryAsyncWithNonExistentDeviceThrowsScannerNotFoundException()
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
    public async Task Naps2ScannerServiceScanAsyncWhenPreCancelledThrowsOperationCanceledException()
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

