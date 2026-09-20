using PhotoCropper.Gui.Services;

namespace PhotoCropper.Gui.Tests.Gui;

public sealed class LocalizationManagerTests
{
    [Fact]
    public void GetAvailableLanguages_ShouldContainExpectedLanguages()
    {
        var languages = LocalizationManager.GetAvailableLanguages();

        languages.ShouldNotBeEmpty();
        languages.ShouldContain(l => l.Code == "en-US" && l.Name == "English");
        languages.ShouldContain(l => l.Code == "es-ES" && l.Name == "Español");
        languages.ShouldContain(l => l.Code == "ca-ES" && l.Name == "Català");
    }

    [Theory]
    [InlineData("es-ES", "es-ES")]
    [InlineData("ca-ES", "ca-ES")]
    [InlineData("en-US", "en-US")]
    [InlineData("invalid-code", "en-US")]
    [InlineData("", "en-US")]
    public void SetLanguage_ShouldUpdateCurrentLanguageAndPersist(string inputCode, string expectedResult)
    {
        LocalizationManager.SetLanguage(inputCode);

        LocalizationManager.CurrentLanguage.ShouldBe(expectedResult);
        SettingsManager.Instance.Settings.Language.ShouldBe(expectedResult);
    }

    [Fact]
    public void Initialize_WithPreferredLanguage_ShouldUsePreferred()
    {
        LocalizationManager.Initialize("ca-ES");

        LocalizationManager.CurrentLanguage.ShouldBe("ca-ES");
        SettingsManager.Instance.Settings.Language.ShouldBe("ca-ES");
    }

    [Fact]
    public void Initialize_WithUnknownPreferredLanguage_ShouldFallbackToDefault()
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ja-JP");
            LocalizationManager.Initialize("xx-YY");

            LocalizationManager.CurrentLanguage.ShouldBe("en-US");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("es-ES")]
    [InlineData("ca-ES")]
    public void NamingAndMetadataKeys_ShouldExistInAllLanguages(string langCode)
    {
        string[] requiredKeys = [
            "LblFileNamePattern",
            "LblNamingPreview",
            "LblMetadata",
            "LblYear",
            "LblNotes",
            "WatermarkYear",
            "WatermarkDescription",
            "ChkApplyYearToAll"
        ];

        string axamlPath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "PhotoCropper.Gui", "Assets", "i18n", $"{langCode}.axaml");

        File.Exists(axamlPath).ShouldBeTrue($"Dictionary file {langCode}.axaml should exist");
        string content = File.ReadAllText(axamlPath);

        foreach (string key in requiredKeys)
        {
            content.ShouldContain($"x:Key=\"{key}\"");
        }
    }
}
