using System;
using System.IO;
using PhotoCropperGui;
using Xunit;

namespace PhotoCropper.Tests;

public sealed class SettingsManagerTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _tempSettingsPath;

    public SettingsManagerTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"PhotoCropperSettingsTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirectory);
        _tempSettingsPath = Path.Combine(_tempDirectory, "settings_test.json");
    }

    [Fact]
    public void SettingsManager_DefaultInitialization()
    {
        var manager = new SettingsManager(_tempSettingsPath);
        manager.Load();

        Assert.NotNull(manager.Settings);
        Assert.Equal("en-US", manager.Settings.Language);
        Assert.Equal(50, manager.Settings.BackgroundTolerance);
        Assert.Equal(1, manager.Settings.MinAreaFactor);
        Assert.Equal(90, manager.Settings.MaxAreaFactor);
        Assert.Equal(20, manager.Settings.CannyLowThreshold);
        Assert.Equal(1, manager.Settings.ZoomLevel);
        Assert.False(manager.Settings.AdvancedVisible);
    }

    [Fact]
    public void SettingsManager_SaveAndLoad_ShouldPersistValues()
    {
        var manager = new SettingsManager(_tempSettingsPath);
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
        var secondManager = new SettingsManager(_tempSettingsPath);
        secondManager.Load();

        Assert.Equal("ca-ES", secondManager.Settings.Language);
        Assert.Equal(42, secondManager.Settings.BackgroundTolerance);
        Assert.Equal(5, secondManager.Settings.MinAreaFactor);
        Assert.Equal(85, secondManager.Settings.MaxAreaFactor);
        Assert.Equal(35, secondManager.Settings.CannyLowThreshold);
        Assert.Equal(2.5, secondManager.Settings.ZoomLevel);
        Assert.True(secondManager.Settings.AdvancedVisible);
        Assert.Equal("C:\\CroppedPhotos", secondManager.Settings.CustomOutputDirectory);
    }

    [Fact]
    public void SettingsManager_ResetDetectionDefaults_ShouldOnlyResetDetectionParameters()
    {
        var manager = new SettingsManager(_tempSettingsPath);
        manager.Load();

        // Set custom settings
        manager.Settings.Language = "es-ES";
        manager.Settings.ZoomLevel = 3.0;
        manager.Settings.AdvancedVisible = true;
        
        manager.Settings.BackgroundTolerance = 15;
        manager.Settings.MinAreaFactor = 12;
        manager.Settings.MaxAreaFactor = 75;
        manager.Settings.CannyLowThreshold = 40;

        manager.Save();

        // Perform detection reset
        manager.ResetDetectionDefaults();

        // Verify detection settings are reset
        Assert.Equal(50, manager.Settings.BackgroundTolerance);
        Assert.Equal(1, manager.Settings.MinAreaFactor);
        Assert.Equal(90, manager.Settings.MaxAreaFactor);
        Assert.Equal(20, manager.Settings.CannyLowThreshold);

        // Verify other user experience settings are intact
        Assert.Equal("es-ES", manager.Settings.Language);
        Assert.Equal(3.0, manager.Settings.ZoomLevel);
        Assert.True(manager.Settings.AdvancedVisible);
    }

    [Fact]
    public void SettingsManager_GracefulDegradation_OnCorruptJson()
    {
        // Write invalid corrupted json text to file
        File.WriteAllText(_tempSettingsPath, "{ INVALID JSON CORRUPTED TEXT ]");

        var manager = new SettingsManager(_tempSettingsPath);
        
        // This should not throw an exception, but gracefully load defaults
        var exception = Record.Exception(() => manager.Load());
        Assert.Null(exception);

        Assert.NotNull(manager.Settings);
        Assert.Equal("en-US", manager.Settings.Language);
        Assert.Equal(50, manager.Settings.BackgroundTolerance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
