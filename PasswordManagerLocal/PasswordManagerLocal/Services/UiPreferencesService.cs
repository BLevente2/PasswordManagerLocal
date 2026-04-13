using Avalonia;
using Avalonia.Styling;
using PasswordManagerLocal.Localization;

namespace PasswordManagerLocal.Services;

public sealed class UiPreferencesService
{
    public event EventHandler<UiPreferencesChangedEventArgs>? PreferencesChanged;

    private AppLanguage _currentLanguage = AppLanguage.Hungarian;
    private AppThemeMode _currentThemeMode = AppThemeMode.Dark;

    public UiPreferencesService()
    {
        ApplyTheme(_currentThemeMode);
    }

    public AppLanguage CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (value == _currentLanguage)
            {
                return;
            }

            var previousTheme = _currentThemeMode;
            _currentLanguage = value;
            PreferencesChanged?.Invoke(this, new UiPreferencesChangedEventArgs(true, false, value, previousTheme));
        }
    }

    public AppThemeMode CurrentThemeMode
    {
        get => _currentThemeMode;
        set
        {
            if (value == _currentThemeMode)
            {
                return;
            }

            _currentThemeMode = value;
            ApplyTheme(value);
            PreferencesChanged?.Invoke(this, new UiPreferencesChangedEventArgs(false, true, _currentLanguage, value));
        }
    }

    public string GetString(string key) => LocalizationManager.GetString(_currentLanguage, key);

    private static void ApplyTheme(AppThemeMode mode)
    {
        if (Application.Current is not Application app)
        {
            return;
        }

        app.RequestedThemeVariant = mode switch
        {
            AppThemeMode.Light => ThemeVariant.Light,
            AppThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }
}

public sealed class UiPreferencesChangedEventArgs : EventArgs
{
    public UiPreferencesChangedEventArgs(bool languageChanged, bool themeChanged, AppLanguage language, AppThemeMode theme)
    {
        LanguageChanged = languageChanged;
        ThemeChanged = themeChanged;
        Language = language;
        Theme = theme;
    }

    public bool LanguageChanged { get; }

    public bool ThemeChanged { get; }

    public AppLanguage Language { get; }

    public AppThemeMode Theme { get; }
}
