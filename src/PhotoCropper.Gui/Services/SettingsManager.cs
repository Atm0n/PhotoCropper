using PhotoCropper.Core.Common;
using System.Runtime.CompilerServices;
using System.Text.Json;

[assembly: InternalsVisibleTo("PhotoCropper.Tests")]

namespace PhotoCropper.Gui.Services;

internal sealed class UserSettings
{
    public string Language { get; set; } = AppConstants.DefaultLanguage;
    public double BackgroundTolerance { get; set; } = 25;
    public double MinAreaFactor { get; set; } = 25; // 25%
    public double MaxAreaFactor { get; set; } = 90; // 90%
    public double CannyLowThreshold { get; set; } = AppConstants.DefaultCannyLow;
    public double ZoomLevel { get; set; } = 1;
    public bool AdvancedVisible { get; set; }
    public string? CustomOutputDirectory { get; set; }
    public string PreferredFormat { get; set; } = AppConstants.FormatJpeg; // JPEG or PNG
    public int JpegQuality { get; set; } = AppConstants.DefaultJpegQuality; // 1-100 (default: 100 maximum quality)
    public bool AutoOrientPhotos { get; set; } = true;
    public bool RestoreVintageColors { get; set; } = true;
    public bool RemoveDustAndScratches { get; set; } = true;
    public bool AutoTuneOnScanChange { get; set; }
    public bool AutoAdjustOnLowCoverage { get; set; } = true;
    public string? WorkDirectory { get; set; }
    public string? SelectedScannerId { get; set; }
    public int ScannerDpi { get; set; } = AppConstants.DefaultScannerDpi;
    public string ScannerPageSize { get; set; } = AppConstants.DefaultScannerPageSize;
    public bool IncludeNetworkScanners { get; set; }
    public string Theme { get; set; } = AppConstants.DefaultTheme; // "Dark", "Light", "System"
    public string DetectionBoxColor { get; set; } = AppConstants.DefaultBoundingBoxColor; // "Red", "Amber", "Cyan", "Lime", "Magenta"
    public string FileNamePattern { get; set; } = AppConstants.DefaultNamingPattern;
    public int? DefaultYear { get; set; }
    public string? DefaultDescription { get; set; }
    public bool ApplyYearToAllScans { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public bool FlashTaskbarOnCompletion { get; set; } = true;
    public bool PlaySoundOnCompletion { get; set; }
    public bool PromptBeforeExport { get; set; }
    public DateTime? LastUpdateCheckUtc { get; set; }
    public bool CheckForUpdatesAutomatically { get; set; } = true;
}

internal sealed class SettingsManager
{
    private static readonly Lazy<SettingsManager> _instance = new(() => new SettingsManager());
    public static SettingsManager Instance => _instance.Value;

    private readonly string _settingsDirectory;
    private readonly string _settingsFilePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public UserSettings Settings { get; private set; } = new();

    // Public constructor for testing/mocking
    public SettingsManager(string? customPath = null)
    {
        if (customPath != null)
        {
            _settingsFilePath = customPath;
            _settingsDirectory = Path.GetDirectoryName(customPath) ?? "";
        }
        else
        {
            _settingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhotoCropper");
            _settingsFilePath = Path.Combine(_settingsDirectory, "settings.json");
        }

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<UserSettings>(json, _jsonOptions);
                if (loaded != null)
                {
                    Settings = loaded;
                    return;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // Fail gracefully - fall through to defaults
        }

        // Default settings if file does not exist or fails to load
        Settings = new UserSettings();
    }

    public void Save()
    {
        try
        {
            if (!string.IsNullOrEmpty(_settingsDirectory) && !Directory.Exists(_settingsDirectory))
            {
                Directory.CreateDirectory(_settingsDirectory);
            }

            string json = JsonSerializer.Serialize(Settings, _jsonOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // Fail silently or handle appropriately
        }
    }

    public void ResetToDefaults()
    {
        Settings = new UserSettings();
        Save();
    }

    public void ResetDetectionDefaults()
    {
        var defaults = new UserSettings();
        Settings.BackgroundTolerance = defaults.BackgroundTolerance;
        Settings.MinAreaFactor = defaults.MinAreaFactor;
        Settings.MaxAreaFactor = defaults.MaxAreaFactor;
        Settings.CannyLowThreshold = defaults.CannyLowThreshold;
        Settings.AutoOrientPhotos = defaults.AutoOrientPhotos;
        Settings.RestoreVintageColors = defaults.RestoreVintageColors;
        Settings.RemoveDustAndScratches = defaults.RemoveDustAndScratches;
        Settings.AutoTuneOnScanChange = defaults.AutoTuneOnScanChange;
        Settings.AutoAdjustOnLowCoverage = defaults.AutoAdjustOnLowCoverage;
        Save();
    }
}
