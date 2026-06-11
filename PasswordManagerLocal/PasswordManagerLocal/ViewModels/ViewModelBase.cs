using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using PasswordManagerLocal.Localization;
using PasswordManagerLocal.Services;
using PasswordManagerLocalBackend.Exceptions;
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


    protected string GetSafeErrorMessage(Exception exception) =>
        exception switch
        {
            InvalidTokenException => GetTranslation("Error_InvalidSession"),
            UserNotFoundException => GetTranslation("Error_InvalidCredentials"),
            UnauthorizedAccessException => GetTranslation("Error_InvalidCredentials"),
            InvalidInputException => GetTranslation("Error_InvalidInput"),
            PasswordNotFoundException => GetTranslation("Error_NotFound"),
            LimitReachedException => GetTranslation("Error_LimitReached"),
            InvalidDataIntegrityException => GetTranslation("Error_DataIntegrity"),
            DeviceIdentityNotInitilaizedException => GetTranslation("Error_DeviceIdentity"),
            OperationCanceledException => GetTranslation("Error_OperationCanceled"),
            _ => GetTranslation("Error_Generic")
        };



    protected static async Task<bool> TryCopyTextToClipboardAsync(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
            return true;
        }

        return false;
    }

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
