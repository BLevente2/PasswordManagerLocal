using Avalonia;
using Avalonia.Styling;
using PasswordManagerLocal.Localization;
using ReactiveUI;
using System.Reactive;

namespace PasswordManagerLocal.ViewModels;

public abstract class ViewModelBase : ReactiveObject
{
    private AppLanguage _currentLanguage = AppLanguage.Hungarian;
    private AppThemeMode _currentThemeMode = AppThemeMode.Dark;

    protected ViewModelBase()
    {
        SetHungarianLanguageCommand = ReactiveCommand.Create<Unit, Unit>(
            _ =>
            {
                CurrentLanguage = AppLanguage.Hungarian;
                return Unit.Default;
            });

        SetEnglishLanguageCommand = ReactiveCommand.Create<Unit, Unit>(
            _ =>
            {
                CurrentLanguage = AppLanguage.English;
                return Unit.Default;
            });

        SetLightThemeCommand = ReactiveCommand.Create<Unit, Unit>(
            _ =>
            {
                CurrentThemeMode = AppThemeMode.Light;
                return Unit.Default;
            });

        SetDarkThemeCommand = ReactiveCommand.Create<Unit, Unit>(
            _ =>
            {
                CurrentThemeMode = AppThemeMode.Dark;
                return Unit.Default;
            });

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

            this.RaiseAndSetIfChanged(ref _currentLanguage, value);

            RaiseBaseTranslationPropertiesChanged();
            OnLanguageChanged();
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

            this.RaiseAndSetIfChanged(ref _currentThemeMode, value);
            ApplyTheme(_currentThemeMode);
        }
    }

    public ReactiveCommand<Unit, Unit> SetHungarianLanguageCommand { get; }

    public ReactiveCommand<Unit, Unit> SetEnglishLanguageCommand { get; }

    public ReactiveCommand<Unit, Unit> SetLightThemeCommand { get; }

    public ReactiveCommand<Unit, Unit> SetDarkThemeCommand { get; }

    public string AppTitle => GetTranslation("AppTitle");

    public string SettingsLabel => GetTranslation("Settings");

    public string SettingsLanguageLabel => GetTranslation("Settings_Language");

    public string SettingsThemeLabel => GetTranslation("Settings_Theme");

    public string EnglishLanguageDisplayName => GetTranslation("Language_English");

    public string HungarianLanguageDisplayName => GetTranslation("Language_Hungarian");

    public string ThemeLightLabel => GetTranslation("Theme_Light");

    public string ThemeDarkLabel => GetTranslation("Theme_Dark");

    protected virtual void OnLanguageChanged()
    {
        // Leszármazott viewmodel-ek itt frissíthetik a saját, lokalizált property-jeiket.
    }

    protected void RaiseBaseTranslationPropertiesChanged()
    {
        this.RaisePropertyChanged(nameof(AppTitle));
        this.RaisePropertyChanged(nameof(SettingsLabel));
        this.RaisePropertyChanged(nameof(SettingsLanguageLabel));
        this.RaisePropertyChanged(nameof(SettingsThemeLabel));
        this.RaisePropertyChanged(nameof(EnglishLanguageDisplayName));
        this.RaisePropertyChanged(nameof(HungarianLanguageDisplayName));
        this.RaisePropertyChanged(nameof(ThemeLightLabel));
        this.RaisePropertyChanged(nameof(ThemeDarkLabel));
    }

    protected string GetTranslation(string key)
    {
        return LocalizationManager.GetString(CurrentLanguage, key);
    }

    protected void ApplyTheme(AppThemeMode mode)
    {
        if (Application.Current is not Application app)
        {
            return;
        }

        var themeVariant = mode switch
        {
            AppThemeMode.Light => ThemeVariant.Light,
            AppThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Light
        };

        app.RequestedThemeVariant = themeVariant;
    }
}