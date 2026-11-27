using PhotoCropper.Gui.Services;

namespace PhotoCropper.Gui.Tests.Services;

public sealed class LocalizationServiceTests
{
    [Fact]
    public void GetStringWhenApplicationNotInitializedShouldReturnFallback()
    {
        string result = LocalizationService.GetString("NonExistentKey", "Fallback Text");
        result.ShouldBe("Fallback Text");
    }

    [Fact]
    public void FormatWhenApplicationNotInitializedShouldFormatFallback()
    {
        string result = LocalizationService.Format("NonExistentKey", "Hello {0} of {1}", 1, 5);
        result.ShouldBe("Hello 1 of 5");
    }

    [Fact]
    public void GetStringWithNullKeyShouldThrow()
    {
        Should.Throw<ArgumentNullException>(() => LocalizationService.GetString(null!));
    }

    [Fact]
    public void FormatWithNullFallbackTemplateShouldThrow()
    {
        Should.Throw<ArgumentNullException>(() => LocalizationService.Format("Key", null!));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("es-ES")]
    [InlineData("ca-ES")]
    public void ResourceKeysAllDeclaredKeysShouldExistInLanguageDictionaries(string langCode)
    {
        string axamlPath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "PhotoCropper.Gui", "Assets", "i18n", $"{langCode}.axaml");

        File.Exists(axamlPath).ShouldBeTrue($"Dictionary file {langCode}.axaml should exist");
        string content = File.ReadAllText(axamlPath);

        // Get all public/internal const string fields in ResourceKeys
        var fields = typeof(ResourceKeys)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string));

        foreach (var field in fields)
        {
            string keyName = (string)field.GetValue(null)!;
            // Skip brush resources which are defined in Fluent theme or App.axaml rather than i18n
            if (keyName.EndsWith("Brush", StringComparison.Ordinal))
            {
                continue;
            }

            content.Contains($"x:Key=\"{keyName}\"", StringComparison.Ordinal).ShouldBeTrue($"Key '{keyName}' should be defined in {langCode}.axaml");
        }
    }

    [Fact]
    public void GetAvailableLanguagesShouldContainExpectedLanguages()
    {
        var languages = LocalizationService.GetAvailableLanguages();

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
    public void SetLanguageShouldUpdateCurrentLanguageAndPersist(string inputCode, string expectedResult)
    {
        LocalizationService.SetLanguage(inputCode);

        LocalizationService.CurrentLanguage.ShouldBe(expectedResult);
        SettingsManager.Instance.Settings.Language.ShouldBe(expectedResult);
    }

    [Fact]
    public void InitializeWithPreferredLanguageShouldUsePreferred()
    {
        LocalizationService.Initialize("ca-ES");

        LocalizationService.CurrentLanguage.ShouldBe("ca-ES");
        SettingsManager.Instance.Settings.Language.ShouldBe("ca-ES");
    }

    [Fact]
    public void InitializeWithUnknownPreferredLanguageShouldFallbackToDefault()
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ja-JP");
            LocalizationService.Initialize("xx-YY");

            LocalizationService.CurrentLanguage.ShouldBe("en-US");
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
    public void NamingAndMetadataKeysShouldExistInAllLanguages(string langCode)
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
