using PasswordManagerLocal.Frontend.Localization;
using PasswordManagerLocal.Backend.Constants;
using System.Globalization;
using System.Text;
using System.Text.Json;


namespace PasswordManagerLocal.Frontend.Services;

public static class AppConfigurationManager
{
    private static readonly object Lock = new();
    private static AppConfiguration? _current;
    private static string? _applicationDataDirectory;

    public static string ConfigurationPath => Path.Combine(
        _applicationDataDirectory ?? throw new InvalidOperationException("Application configuration has not been initialized."),
        ApplicationFileNames.AppConfigFileName);

    public static void Initialize(string applicationDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataDirectory);

        lock (Lock)
        {
            var fullPath = Path.GetFullPath(applicationDataDirectory);
            if (_applicationDataDirectory is not null &&
                !string.Equals(_applicationDataDirectory, fullPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Application configuration was already initialized for another directory.");
            }

            Directory.CreateDirectory(fullPath);
            _applicationDataDirectory = fullPath;
        }
    }

    public static (AppLanguage Language, AppThemeMode Theme) GetUiPreferences()
    {
        lock (Lock)
        {
            var configuration = GetOrCreateConfiguration();
            return (ParseLanguage(configuration.Language), ParseTheme(configuration.Theme));
        }
    }

    public static bool IsWindowsFirewallConfigured()
    {
        lock (Lock)
        {
            return GetOrCreateConfiguration().WindowsFirewallConfigured;
        }
    }

    public static void SaveUiPreferences(AppLanguage language, AppThemeMode theme)
    {
        lock (Lock)
        {
            var configuration = GetOrCreateConfiguration();
            configuration.Language = language.ToString();
            configuration.Theme = theme.ToString();
            SaveConfiguration(configuration);
        }
    }

    public static void SetWindowsFirewallConfigured(bool isConfigured)
    {
        lock (Lock)
        {
            var configuration = GetOrCreateConfiguration();
            if (configuration.WindowsFirewallConfigured == isConfigured)
                return;

            configuration.WindowsFirewallConfigured = isConfigured;
            SaveConfiguration(configuration);
        }
    }

    private static AppConfiguration GetOrCreateConfiguration()
    {
        if (_current is not null)
            return _current;

        var configuration = TryLoadConfiguration(
            out var loadedSuccessfully,
            out var configurationShapeRequiresRewrite) ??
            CreateDefaultConfiguration();
        var normalized = NormalizeConfiguration(configuration);
        _current = normalized.Configuration;

        if (!loadedSuccessfully || configurationShapeRequiresRewrite || normalized.WasChanged)
            SaveConfiguration(_current);

        return _current;
    }

    private static AppConfiguration? TryLoadConfiguration(
        out bool loadedSuccessfully,
        out bool configurationShapeRequiresRewrite)
    {
        loadedSuccessfully = false;
        configurationShapeRequiresRewrite = false;

        try
        {
            if (!File.Exists(ConfigurationPath))
                return null;

            var json = File.ReadAllText(ConfigurationPath, Encoding.UTF8);
            using var document = JsonDocument.Parse(json);
            configurationShapeRequiresRewrite = RequiresConfigurationRewrite(document.RootElement);

            var configuration = JsonSerializer.Deserialize(
                json,
                AppConfigurationJsonContext.Default.AppConfiguration);
            loadedSuccessfully = configuration is not null;
            return configuration;
        }
        catch
        {
            return null;
        }
    }



    private static bool RequiresConfigurationRewrite(JsonElement root) =>
        root.ValueKind != JsonValueKind.Object ||
        !root.TryGetProperty("language", out _) ||
        !root.TryGetProperty("theme", out _) ||
        !root.TryGetProperty("windowsFirewallConfigured", out _);

    private static AppConfiguration CreateDefaultConfiguration() =>
        new()
        {
            Language = DetectDefaultLanguage().ToString(),
            Theme = AppThemeMode.Dark.ToString(),
            WindowsFirewallConfigured = false
        };

    private static (AppConfiguration Configuration, bool WasChanged) NormalizeConfiguration(
        AppConfiguration configuration)
    {
        var language = ParseLanguage(configuration.Language);
        var theme = ParseTheme(configuration.Theme);
        var normalizedLanguage = language.ToString();
        var normalizedTheme = theme.ToString();
        var wasChanged =
            !string.Equals(configuration.Language, normalizedLanguage, StringComparison.Ordinal) ||
            !string.Equals(configuration.Theme, normalizedTheme, StringComparison.Ordinal);

        configuration.Language = normalizedLanguage;
        configuration.Theme = normalizedTheme;
        return (configuration, wasChanged);
    }

    private static AppLanguage ParseLanguage(string? value)
    {
        if (Enum.TryParse<AppLanguage>(value, true, out var language) &&
            language is AppLanguage.English or AppLanguage.Hungarian)
        {
            return language;
        }

        return DetectDefaultLanguage();
    }

    private static AppThemeMode ParseTheme(string? value)
    {
        if (Enum.TryParse<AppThemeMode>(value, true, out var theme) &&
            theme is AppThemeMode.Light or AppThemeMode.Dark)
        {
            return theme;
        }

        return AppThemeMode.Dark;
    }

    private static AppLanguage DetectDefaultLanguage()
    {
        try
        {
            if (IsHungarianCulture(CultureInfo.CurrentUICulture) ||
                IsHungarianCulture(CultureInfo.CurrentCulture))
            {
                return AppLanguage.Hungarian;
            }
        }
        catch
        {
        }

        try
        {
            if (string.Equals(
                RegionInfo.CurrentRegion.TwoLetterISORegionName,
                "HU",
                StringComparison.OrdinalIgnoreCase))
            {
                return AppLanguage.Hungarian;
            }
        }
        catch
        {
        }

        return AppLanguage.English;
    }

    private static bool IsHungarianCulture(CultureInfo culture) =>
        string.Equals(culture.TwoLetterISOLanguageName, "hu", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(culture.Name, "hu-HU", StringComparison.OrdinalIgnoreCase);

    private static void SaveConfiguration(AppConfiguration configuration)
    {
        var temporaryPath = $"{ConfigurationPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigurationPath)!);
            var json = JsonSerializer.Serialize(
                configuration,
                AppConfigurationJsonContext.Default.AppConfiguration);
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            File.Move(temporaryPath, ConfigurationPath, true);
        }
        catch
        {
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch
            {
            }
        }
    }
}
