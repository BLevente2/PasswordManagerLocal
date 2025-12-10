using Avalonia.Platform;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace PasswordManagerLocal.Localization;

public static class LocalizationManager
{
    private static readonly Dictionary<AppLanguage, Dictionary<string, string>> _translations =
        new();

    static LocalizationManager()
    {
        LoadLanguage(AppLanguage.English,
            "avares://PasswordManagerLocal/Assets/Localization/en_us.json");

        LoadLanguage(AppLanguage.Hungarian,
            "avares://PasswordManagerLocal/Assets/Localization/hu.json");
    }

    private static void LoadLanguage(AppLanguage language, string assetUriString)
    {
        try
        {
            var uri = new Uri(assetUriString);

            if (!AssetLoader.Exists(uri))
            {
                return;
            }

            using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: false);

            var json = reader.ReadToEnd();

            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            if (dict is not null)
            {
                _translations[language] = dict;
            }
        }
        catch
        {
        }
    }

    public static string GetString(AppLanguage language, string key)
    {
        if (_translations.TryGetValue(language, out var langDict) &&
            langDict.TryGetValue(key, out var value))
        {
            return value;
        }

        if (language != AppLanguage.English &&
            _translations.TryGetValue(AppLanguage.English, out var enDict) &&
            enDict.TryGetValue(key, out var enValue))
        {
            return enValue;
        }

        return key;
    }
}