using Avalonia;
using Avalonia.Styling;
using PasswordManagerLocal.Frontend.Localization;

namespace PasswordManagerLocal.Frontend.Services;

public sealed class UiPreferencesService
{
    public event EventHandler<UiPreferencesChangedEventArgs>? PreferencesChanged;

    private AppLanguage _currentLanguage;
    private AppThemeMode _currentThemeMode;

    public UiPreferencesService()
    {
        var preferences = AppConfigurationManager.GetUiPreferences();
        _currentLanguage = preferences.Language;
        _currentThemeMode = preferences.Theme;
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

            _currentLanguage = value;
            AppConfigurationManager.SaveUiPreferences(_currentLanguage, _currentThemeMode);
            PreferencesChanged?.Invoke(this, new UiPreferencesChangedEventArgs(true, false, value, _currentThemeMode));
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
            AppConfigurationManager.SaveUiPreferences(_currentLanguage, _currentThemeMode);
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
