using PhotoCropper.TestHelpers;

namespace PhotoCropper.Gui.Tests.Gui;

public sealed class SettingsManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsPath;

    public SettingsManagerTests()
    {
        _tempDir = TestImageFactory.CreateTempDirectory("SettingsTests");
        _settingsPath = Path.Combine(_tempDir, "settings_test.json");
    }

    [Fact]
    public void SettingsManager_DefaultInitialization()
    {
        var manager = new SettingsManager(_settingsPath);
        manager.Load();

        manager.Settings.ShouldNotBeNull();
        manager.Settings.Language.ShouldBe("en-US");
        manager.Settings.BackgroundTolerance.ShouldBe(25);
        manager.Settings.MinAreaFactor.ShouldBe(25);
        manager.Settings.MaxAreaFactor.ShouldBe(90);
        manager.Settings.CannyLowThreshold.ShouldBe(20);
        manager.Settings.ZoomLevel.ShouldBe(1);
        manager.Settings.AdvancedVisible.ShouldBeFalse();
        manager.Settings.AutoOrientPhotos.ShouldBeTrue();
        manager.Settings.RestoreVintageColors.ShouldBeTrue();
        manager.Settings.RemoveDustAndScratches.ShouldBeTrue();
        manager.Settings.Theme.ShouldBe("Dark");
        manager.Settings.DetectionBoxColor.ShouldBe("Red");
        manager.Settings.FileNamePattern.ShouldBe("{original}_{index}");
        manager.Settings.JpegQuality.ShouldBe(100);
        manager.Settings.DefaultYear.ShouldBeNull();
        manager.Settings.DefaultDescription.ShouldBeNull();
        manager.Settings.ApplyYearToAllScans.ShouldBeTrue();
    }

    [Fact]
    public void SettingsManager_SaveAndLoad_ShouldPersistValues()
    {
        var manager = new SettingsManager(_settingsPath);
        manager.Load();

        // Modify values
        manager.Settings.Language = "ca-ES";
        manager.Settings.BackgroundTolerance = 42;
        manager.Settings.MinAreaFactor = 5;
        manager.Settings.MaxAreaFactor = 85;
        manager.Settings.CannyLowThreshold = 35;
        manager.Settings.ZoomLevel = 2.5;
        manager.Settings.AdvancedVisible = true;
        manager.Settings.CustomOutputDirectory = "C:\\CroppedPhotos";
        manager.Settings.WorkDirectory = "C:\\ScannerWorkspace";
        manager.Settings.SelectedScannerId = "canon-lide-400";
        manager.Settings.ScannerDpi = 600;
        manager.Settings.Theme = "Light";
        manager.Settings.DetectionBoxColor = "Amber";
        manager.Settings.FileNamePattern = "{year}_{original}_{index:02}";
        manager.Settings.DefaultYear = 1985;
        manager.Settings.DefaultDescription = "Family Album";
        manager.Settings.ApplyYearToAllScans = false;

        manager.Save();

        // Create new manager instance referencing the same file
        var secondManager = new SettingsManager(_settingsPath);
        secondManager.Load();

        secondManager.Settings.Language.ShouldBe("ca-ES");
        secondManager.Settings.BackgroundTolerance.ShouldBe(42);
        secondManager.Settings.MinAreaFactor.ShouldBe(5);
        secondManager.Settings.MaxAreaFactor.ShouldBe(85);
        secondManager.Settings.CannyLowThreshold.ShouldBe(35);
        secondManager.Settings.ZoomLevel.ShouldBe(2.5);
        secondManager.Settings.AdvancedVisible.ShouldBeTrue();
        secondManager.Settings.CustomOutputDirectory.ShouldBe("C:\\CroppedPhotos");
        secondManager.Settings.WorkDirectory.ShouldBe("C:\\ScannerWorkspace");
        secondManager.Settings.SelectedScannerId.ShouldBe("canon-lide-400");
        secondManager.Settings.ScannerDpi.ShouldBe(600);
        secondManager.Settings.Theme.ShouldBe("Light");
        secondManager.Settings.DetectionBoxColor.ShouldBe("Amber");
        secondManager.Settings.FileNamePattern.ShouldBe("{year}_{original}_{index:02}");
        secondManager.Settings.DefaultYear.ShouldBe(1985);
        secondManager.Settings.DefaultDescription.ShouldBe("Family Album");
        secondManager.Settings.ApplyYearToAllScans.ShouldBeFalse();
    }

    [Fact]
    public void DetectionOptions_GetBoundingBoxColorBgr_ShouldReturnCorrectBgrScalars()
    {
        var options = new PhotoCropper.Core.Models.DetectionOptions { BoundingBoxColor = "Red" };
        options.GetBoundingBoxColorBgr().V2.ShouldBe(255); // Red channel in BGR

        options.BoundingBoxColor = "Amber";
        var amber = options.GetBoundingBoxColorBgr();
        amber.V1.ShouldBe(165); // Green channel in BGR
        amber.V2.ShouldBe(255); // Red channel

        options.BoundingBoxColor = "Cyan";
        var cyan = options.GetBoundingBoxColorBgr();
        cyan.V0.ShouldBe(255); // Blue channel in BGR
        cyan.V1.ShouldBe(255); // Green channel

        options.BoundingBoxColor = "Lime";
        var lime = options.GetBoundingBoxColorBgr();
        lime.V1.ShouldBe(255); // Green channel

        options.BoundingBoxColor = "Magenta";
        var magenta = options.GetBoundingBoxColorBgr();
        magenta.V0.ShouldBe(255); // Blue
        magenta.V2.ShouldBe(255); // Red

        options.BoundingBoxColor = "#112233";
        var custom = options.GetBoundingBoxColorBgr();
        custom.V2.ShouldBe(0x11); // Red
        custom.V1.ShouldBe(0x22); // Green
        custom.V0.ShouldBe(0x33); // Blue
    }

    [Fact]
    public void SettingsManager_ResetDetectionDefaults_ShouldOnlyResetDetectionParameters()
    {
        var manager = new SettingsManager(_settingsPath);
        manager.Load();

        manager.Settings.Language = "es-ES";
        manager.Settings.ZoomLevel = 3.0;
        manager.Settings.AdvancedVisible = true;

        manager.Settings.BackgroundTolerance = 15;
        manager.Settings.MinAreaFactor = 12;
        manager.Settings.MaxAreaFactor = 75;
        manager.Settings.CannyLowThreshold = 40;

        manager.Save();
        manager.ResetDetectionDefaults();

        manager.Settings.BackgroundTolerance.ShouldBe(25);
        manager.Settings.MinAreaFactor.ShouldBe(25);
        manager.Settings.MaxAreaFactor.ShouldBe(90);
        manager.Settings.CannyLowThreshold.ShouldBe(20);

        manager.Settings.Language.ShouldBe("es-ES");
        manager.Settings.ZoomLevel.ShouldBe(3.0);
        manager.Settings.AdvancedVisible.ShouldBeTrue();
    }

    [Fact]
    public void SettingsManager_GracefulDegradation_OnCorruptJson()
    {
        File.WriteAllText(_settingsPath, "{ INVALID JSON CORRUPTED TEXT ]");

        var manager = new SettingsManager(_settingsPath);
        var exception = Record.Exception(() => manager.Load());
        exception.ShouldBeNull();

        manager.Settings.ShouldNotBeNull();
        manager.Settings.Language.ShouldBe("en-US");
        manager.Settings.BackgroundTolerance.ShouldBe(25);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
