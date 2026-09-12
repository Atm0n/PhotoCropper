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
        manager.Settings.MinAreaFactor.ShouldBe(15);
        manager.Settings.MaxAreaFactor.ShouldBe(90);
        manager.Settings.CannyLowThreshold.ShouldBe(20);
        manager.Settings.ZoomLevel.ShouldBe(1);
        manager.Settings.AdvancedVisible.ShouldBeFalse();
        manager.Settings.AutoOrientPhotos.ShouldBeTrue();
        manager.Settings.RestoreVintageColors.ShouldBeTrue();
        manager.Settings.RemoveDustAndScratches.ShouldBeTrue();
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
        manager.Settings.MinAreaFactor.ShouldBe(15);
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
