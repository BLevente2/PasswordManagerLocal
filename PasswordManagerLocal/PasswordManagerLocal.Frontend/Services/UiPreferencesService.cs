using Avalonia;
using Avalonia.Styling;
using PasswordManagerLocal.Contracts.Preferences;
using PasswordManagerLocal.Frontend.Localization;
using PasswordManagerLocal.Preferences;

namespace PasswordManagerLocal.Frontend.Services;

public sealed class UiPreferencesService
{
    private readonly IApplicationPreferencesStore _store;
    private ApplicationPreferences _current;

    public UiPreferencesService(IApplicationPreferencesStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _current = _store.ReadAsync().GetAwaiter().GetResult();
        ApplyTheme(_current.Theme);
    }

    public event EventHandler<UiPreferencesChangedEventArgs>? PreferencesChanged;

    public AppLanguage CurrentLanguage
    {
        get => _current.Language;
        set
        {
            if (value == _current.Language)
                return;

            var updated = _current with { Language = value };
            _store.WriteAsync(updated).GetAwaiter().GetResult();
            _current = updated;
            PreferencesChanged?.Invoke(
                this,
                new UiPreferencesChangedEventArgs(true, false, value, _current.Theme));
        }
    }

    public AppThemeMode CurrentThemeMode
    {
        get => _current.Theme;
        set
        {
            if (value == _current.Theme)
                return;

            var updated = _current with { Theme = value };
            _store.WriteAsync(updated).GetAwaiter().GetResult();
            _current = updated;
            ApplyTheme(value);
            PreferencesChanged?.Invoke(
                this,
                new UiPreferencesChangedEventArgs(false, true, _current.Language, value));
        }
    }

    public string GetString(string key) => LocalizationManager.GetString(_current.Language, key);

    private static void ApplyTheme(AppThemeMode mode)
    {
        if (Application.Current is not Application app)
            return;

        app.RequestedThemeVariant = mode switch
        {
            AppThemeMode.Light => ThemeVariant.Light,
            AppThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }
}
