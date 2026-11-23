using System.Runtime.CompilerServices;
using PhotoCropper.Gui.Services;

namespace PhotoCropper.Gui.Tests;

/// <summary>
/// Points <see cref="SettingsManager.Instance"/> at a throw-away file before any test runs. Without this, tests that
/// change a setting (e.g. the language) saved the in-memory defaults over the developer's real settings.json.
/// </summary>
internal static class TestSettingsIsolation
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        string path = Path.Combine(Path.GetTempPath(), "PhotoCropperTests", $"settings-{Environment.ProcessId}.json");
        Environment.SetEnvironmentVariable(SettingsManager.SettingsPathVariable, path);
    }
}
