using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PhotoCropperGui;

public static class LocalizationManager
{
    private static readonly Dictionary<string, string> AvailableLanguages = new()
    {
        { "en-US", "English" },
        { "es-ES", "Español" },
        { "ca-ES", "Català" }
    };

    public static string CurrentLanguage { get; private set; } = "en-US";

    public static void SetLanguage(string languageCode)
    {
        if (!AvailableLanguages.ContainsKey(languageCode))
        {
            languageCode = "en-US";
        }

        var translations = Application.Current?.Resources.MergedDictionaries
            .OfType<ResourceInclude>()
            .FirstOrDefault(d => d.Source?.ToString().Contains("i18n") == true);

        if (translations != null)
        {
            Application.Current?.Resources.MergedDictionaries.Remove(translations);
        }

        Application.Current?.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri($"avares://PhotoCropperGui/Assets/i18n/{languageCode}.axaml"))
        {
            Source = new Uri($"avares://PhotoCropperGui/Assets/i18n/{languageCode}.axaml")
        });

        CurrentLanguage = languageCode;

        // Persist language setting
        if (SettingsManager.Instance.Settings.Language != languageCode)
        {
            SettingsManager.Instance.Settings.Language = languageCode;
            SettingsManager.Instance.Save();
        }
    }

    public static void Initialize(string? preferredLanguage = null)
    {
        if (!string.IsNullOrEmpty(preferredLanguage) && AvailableLanguages.ContainsKey(preferredLanguage))
        {
            SetLanguage(preferredLanguage);
            return;
        }

        string localCulture = CultureInfo.CurrentCulture.Name;
        
        // Try to match specific culture (es-ES) or general language (es)
        var match = AvailableLanguages.Keys.FirstOrDefault(k => k.Equals(localCulture, StringComparison.OrdinalIgnoreCase))
                 ?? AvailableLanguages.Keys.FirstOrDefault(k => k.StartsWith(localCulture.Split('-')[0], StringComparison.OrdinalIgnoreCase));

        SetLanguage(match ?? "en-US");
    }

    public static List<(string Code, string Name)> GetAvailableLanguages()
    {
        return AvailableLanguages.Select(kv => (kv.Key, kv.Value)).ToList();
    }
}
