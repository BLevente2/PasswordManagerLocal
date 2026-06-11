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
            DeviceEnrollmentException deviceEnrollmentException => GetDeviceEnrollmentErrorMessage(deviceEnrollmentException),
            OperationCanceledException => GetTranslation("Error_OperationCanceled"),
            _ => GetTranslation("Error_Generic")
        };


    protected string GetDeviceEnrollmentErrorMessage(DeviceEnrollmentException exception)
    {
        var message = GetDeviceEnrollmentErrorMessage(exception.ErrorCode);
        var detail = exception.Message?.Trim();

        if (string.IsNullOrWhiteSpace(detail))
            return message;

        if (detail.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ||
            detail.StartsWith("Hiba:", StringComparison.OrdinalIgnoreCase))
            return message;

        return $"{message}\n\n{GetTranslation("Error_TechnicalDetails")}: {detail}";
    }


    protected string GetDeviceEnrollmentErrorMessage(DeviceEnrollmentErrorCode errorCode) =>
        errorCode switch
        {
            DeviceEnrollmentErrorCode.SyncDisabled => GetTranslation("Error_DeviceEnrollment_SyncDisabled"),
            DeviceEnrollmentErrorCode.InvalidCode => GetTranslation("Error_DeviceEnrollment_InvalidCode"),
            DeviceEnrollmentErrorCode.NewDeviceNotFound => GetTranslation("Error_DeviceEnrollment_NewDeviceNotFound"),
            DeviceEnrollmentErrorCode.NewDeviceConnectionFailed => GetTranslation("Error_DeviceEnrollment_NewDeviceConnectionFailed"),
            DeviceEnrollmentErrorCode.NewDeviceRejected => GetTranslation("Error_DeviceEnrollment_NewDeviceRejected"),
            DeviceEnrollmentErrorCode.CodeExpired => GetTranslation("Error_DeviceEnrollment_CodeExpired"),
            DeviceEnrollmentErrorCode.CodeProofInvalid => GetTranslation("Error_DeviceEnrollment_CodeProofInvalid"),
            DeviceEnrollmentErrorCode.ProfileDataInvalid => GetTranslation("Error_DeviceEnrollment_ProfileDataInvalid"),
            DeviceEnrollmentErrorCode.ProfileDataTooLarge => GetTranslation("Error_DeviceEnrollment_ProfileDataTooLarge"),
            DeviceEnrollmentErrorCode.DeviceIdentityConflict => GetTranslation("Error_DeviceEnrollment_DeviceIdentityConflict"),
            _ => GetTranslation("Error_DeviceEnrollment_Generic")
        };



    protected static Task<bool> TryCopyTextToClipboardAsync(string? text) =>
        ClipboardService.TrySetTextAsync(text);

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
