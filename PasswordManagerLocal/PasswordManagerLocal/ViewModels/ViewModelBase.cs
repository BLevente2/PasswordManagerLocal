using PasswordManagerLocal.Localization;
using PasswordManagerLocal.Services;
using ReactiveUI;

namespace PasswordManagerLocal.ViewModels;

public abstract class ViewModelBase : ReactiveObject
{
    protected ViewModelBase(UiPreferencesService uiPreferences)
    {
        UiPreferences = uiPreferences;
        UiPreferences.PreferencesChanged += HandlePreferencesChanged;
    }

    protected UiPreferencesService UiPreferences { get; }

    public AppLanguage CurrentLanguage => UiPreferences.CurrentLanguage;

    public AppThemeMode CurrentThemeMode => UiPreferences.CurrentThemeMode;

    protected string GetTranslation(string key) => UiPreferences.GetString(key);

    protected virtual void OnLanguageChanged()
    {
    }

    protected virtual void OnThemeChanged()
    {
    }

    private void HandlePreferencesChanged(object? sender, UiPreferencesChangedEventArgs e)
    {
        if (e.LanguageChanged)
        {
            OnLanguageChanged();
        }

        if (e.ThemeChanged)
        {
            OnThemeChanged();
        }
    }
}
