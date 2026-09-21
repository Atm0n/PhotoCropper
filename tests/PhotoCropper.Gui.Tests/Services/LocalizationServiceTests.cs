using PhotoCropper.Gui.Services;
using Shouldly;

namespace PhotoCropper.Gui.Tests.Services;

public sealed class LocalizationServiceTests
{
    [Fact]
    public void GetString_WhenApplicationNotInitialized_ShouldReturnFallback()
    {
        string result = LocalizationService.GetString("NonExistentKey", "Fallback Text");
        result.ShouldBe("Fallback Text");
    }

    [Fact]
    public void Format_WhenApplicationNotInitialized_ShouldFormatFallback()
    {
        string result = LocalizationService.Format("NonExistentKey", "Hello {0} of {1}", 1, 5);
        result.ShouldBe("Hello 1 of 5");
    }

    [Fact]
    public void GetString_WithNullKey_ShouldThrow()
    {
        Should.Throw<ArgumentNullException>(() => LocalizationService.GetString(null!));
    }

    [Fact]
    public void Format_WithNullFallbackTemplate_ShouldThrow()
    {
        Should.Throw<ArgumentNullException>(() => LocalizationService.Format("Key", null!));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("es-ES")]
    [InlineData("ca-ES")]
    public void ResourceKeys_AllDeclaredKeysShouldExistInLanguageDictionaries(string langCode)
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
}
