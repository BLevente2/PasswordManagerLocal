using PasswordManagerLocal.Helpers;
using PasswordManagerLocal.Services;
using PasswordManagerLocalBackend.Abstractions;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;
using ReactiveUI;
using System.Reactive;
using System.Security.Cryptography;

namespace PasswordManagerLocal.ViewModels.Pages;

public sealed class ProfileViewModel : ViewModelBase
{
    private readonly IEndpoints _endpoints;
    private readonly Func<Task> _refreshAuthenticatedStateAsync;
    private readonly Func<Task> _handleAccountDeletedAsync;

    private Guid _token;
    private string _username = string.Empty;
    private string _firstName = string.Empty;
    private string _lastName = string.Empty;
    private string _email = string.Empty;
    private DateTime _registrationDate;
    private DateTime _lastLoginDate;
    private string _editFirstName = string.Empty;
    private string _editLastName = string.Empty;
    private string _editEmail = string.Empty;
    private string _editUsername = string.Empty;
    private string _currentPassword = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmNewPassword = string.Empty;
    private string _deleteAccountPassword = string.Empty;
    private string? _statusMessage;

    public ProfileViewModel(
        UiPreferencesService uiPreferences,
        IEndpoints endpoints,
        Func<Task> refreshAuthenticatedStateAsync,
        Func<Task> handleAccountDeletedAsync)
        : base(uiPreferences)
    {
        _endpoints = endpoints;
        _refreshAuthenticatedStateAsync = refreshAuthenticatedStateAsync;
        _handleAccountDeletedAsync = handleAccountDeletedAsync;

        SaveProfileCommand = ReactiveCommand.CreateFromTask(SaveProfileAsync);
        ChangeUsernameCommand = ReactiveCommand.CreateFromTask(ChangeUsernameAsync);
        ChangeMasterPasswordCommand = ReactiveCommand.CreateFromTask(ChangeMasterPasswordAsync);
        DeleteAccountCommand = ReactiveCommand.CreateFromTask(DeleteAccountAsync);
    }

    public string Username
    {
        get => _username;
        private set => this.RaiseAndSetIfChanged(ref _username, value);
    }

    public string FirstName
    {
        get => _firstName;
        private set => this.RaiseAndSetIfChanged(ref _firstName, value);
    }

    public string LastName
    {
        get => _lastName;
        private set => this.RaiseAndSetIfChanged(ref _lastName, value);
    }

    public string Email
    {
        get => _email;
        private set => this.RaiseAndSetIfChanged(ref _email, value);
    }

    public DateTime RegistrationDate
    {
        get => _registrationDate;
        private set
        {
            this.RaiseAndSetIfChanged(ref _registrationDate, value);
            this.RaisePropertyChanged(nameof(RegistrationDateText));
        }
    }

    public DateTime LastLoginDate
    {
        get => _lastLoginDate;
        private set
        {
            this.RaiseAndSetIfChanged(ref _lastLoginDate, value);
            this.RaisePropertyChanged(nameof(LastLoginDateText));
        }
    }

    public string EditFirstName
    {
        get => _editFirstName;
        set => this.RaiseAndSetIfChanged(ref _editFirstName, value);
    }

    public string EditLastName
    {
        get => _editLastName;
        set => this.RaiseAndSetIfChanged(ref _editLastName, value);
    }

    public string EditEmail
    {
        get => _editEmail;
        set => this.RaiseAndSetIfChanged(ref _editEmail, value);
    }

    public string EditUsername
    {
        get => _editUsername;
        set => this.RaiseAndSetIfChanged(ref _editUsername, value);
    }

    public string CurrentPassword
    {
        get => _currentPassword;
        set => this.RaiseAndSetIfChanged(ref _currentPassword, value);
    }

    public string NewPassword
    {
        get => _newPassword;
        set => this.RaiseAndSetIfChanged(ref _newPassword, value);
    }

    public string ConfirmNewPassword
    {
        get => _confirmNewPassword;
        set => this.RaiseAndSetIfChanged(ref _confirmNewPassword, value);
    }

    public string DeleteAccountPassword
    {
        get => _deleteAccountPassword;
        set => this.RaiseAndSetIfChanged(ref _deleteAccountPassword, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _statusMessage, value);
            this.RaisePropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public string RegistrationDateText => RegistrationDate.ToLocalTime().ToString("f");

    public string LastLoginDateText => LastLoginDate.ToLocalTime().ToString("f");

    public ReactiveCommand<Unit, Unit> SaveProfileCommand { get; }

    public ReactiveCommand<Unit, Unit> ChangeUsernameCommand { get; }

    public ReactiveCommand<Unit, Unit> ChangeMasterPasswordCommand { get; }

    public ReactiveCommand<Unit, Unit> DeleteAccountCommand { get; }

    public string Title => GetTranslation("Profile_Title");

    public string Subtitle => GetTranslation("Profile_Subtitle");

    public string AccountOverviewLabel => GetTranslation("Profile_Overview_Title");

    public string PersonalInfoTitle => GetTranslation("Profile_Personal_Title");

    public string UsernameTitle => GetTranslation("Profile_Username_Title");

    public string SecurityTitle => GetTranslation("Profile_Security_Title");

    public string DangerZoneTitle => GetTranslation("Profile_Danger_Title");

    public string UsernameLabel => GetTranslation("Login_Username_Label");

    public string FirstNameLabel => GetTranslation("Register_FirstName_Label");

    public string LastNameLabel => GetTranslation("Register_LastName_Label");

    public string EmailLabel => GetTranslation("Register_Email_Label");

    public string RegistrationDateLabel => GetTranslation("Profile_RegistrationDate");

    public string LastLoginDateLabel => GetTranslation("Profile_LastLoginDate");

    public string SaveProfileLabel => GetTranslation("Common_Save");

    public string ChangeUsernameLabel => GetTranslation("Profile_ChangeUsername");

    public string ChangeMasterPasswordLabel => GetTranslation("Profile_ChangeMasterPassword");

    public string DeleteAccountLabel => GetTranslation("Profile_DeleteAccount");

    public string CurrentPasswordLabel => GetTranslation("Profile_CurrentPassword");

    public string NewPasswordLabel => GetTranslation("Profile_NewPassword");

    public string ConfirmNewPasswordLabel => GetTranslation("Profile_ConfirmNewPassword");

    public string DeleteAccountDescription => GetTranslation("Profile_Danger_Description");

    public string EditUsernamePlaceholder => GetTranslation("Profile_Username_Placeholder");

    public string CurrentPasswordPlaceholder => GetTranslation("Profile_CurrentPassword_Placeholder");

    public string NewPasswordPlaceholder => GetTranslation("Profile_NewPassword_Placeholder");

    public string ConfirmNewPasswordPlaceholder => GetTranslation("Profile_ConfirmNewPassword_Placeholder");

    public string DeleteAccountPasswordPlaceholder => GetTranslation("Profile_DeletePassword_Placeholder");

    protected override void OnLanguageChanged()
    {
        this.RaisePropertyChanged(nameof(Title));
        this.RaisePropertyChanged(nameof(Subtitle));
        this.RaisePropertyChanged(nameof(AccountOverviewLabel));
        this.RaisePropertyChanged(nameof(PersonalInfoTitle));
        this.RaisePropertyChanged(nameof(UsernameTitle));
        this.RaisePropertyChanged(nameof(SecurityTitle));
        this.RaisePropertyChanged(nameof(DangerZoneTitle));
        this.RaisePropertyChanged(nameof(UsernameLabel));
        this.RaisePropertyChanged(nameof(FirstNameLabel));
        this.RaisePropertyChanged(nameof(LastNameLabel));
        this.RaisePropertyChanged(nameof(EmailLabel));
        this.RaisePropertyChanged(nameof(RegistrationDateLabel));
        this.RaisePropertyChanged(nameof(LastLoginDateLabel));
        this.RaisePropertyChanged(nameof(SaveProfileLabel));
        this.RaisePropertyChanged(nameof(ChangeUsernameLabel));
        this.RaisePropertyChanged(nameof(ChangeMasterPasswordLabel));
        this.RaisePropertyChanged(nameof(DeleteAccountLabel));
        this.RaisePropertyChanged(nameof(CurrentPasswordLabel));
        this.RaisePropertyChanged(nameof(NewPasswordLabel));
        this.RaisePropertyChanged(nameof(ConfirmNewPasswordLabel));
        this.RaisePropertyChanged(nameof(DeleteAccountDescription));
        this.RaisePropertyChanged(nameof(EditUsernamePlaceholder));
        this.RaisePropertyChanged(nameof(CurrentPasswordPlaceholder));
        this.RaisePropertyChanged(nameof(NewPasswordPlaceholder));
        this.RaisePropertyChanged(nameof(ConfirmNewPasswordPlaceholder));
        this.RaisePropertyChanged(nameof(DeleteAccountPasswordPlaceholder));
        this.RaisePropertyChanged(nameof(RegistrationDateText));
        this.RaisePropertyChanged(nameof(LastLoginDateText));
    }

    public void Load(Guid token, UserProfileInfoResponse profile)
    {
        _token = token;
        Username = profile.Username;
        FirstName = profile.FirstName;
        LastName = profile.LastName;
        Email = profile.Email;
        RegistrationDate = profile.RegistrationDate;
        LastLoginDate = profile.LastLoginDate;
        EditUsername = profile.Username;
        EditFirstName = profile.FirstName;
        EditLastName = profile.LastName;
        EditEmail = profile.Email;
        CurrentPassword = string.Empty;
        NewPassword = string.Empty;
        ConfirmNewPassword = string.Empty;
        DeleteAccountPassword = string.Empty;
        StatusMessage = null;
    }

    public void Reset()
    {
        _token = Guid.Empty;
        Username = string.Empty;
        FirstName = string.Empty;
        LastName = string.Empty;
        Email = string.Empty;
        RegistrationDate = DateTime.MinValue;
        LastLoginDate = DateTime.MinValue;
        EditUsername = string.Empty;
        EditFirstName = string.Empty;
        EditLastName = string.Empty;
        EditEmail = string.Empty;
        CurrentPassword = string.Empty;
        NewPassword = string.Empty;
        ConfirmNewPassword = string.Empty;
        DeleteAccountPassword = string.Empty;
        StatusMessage = null;
    }

    private async Task SaveProfileAsync()
    {
        if (_token == Guid.Empty)
        {
            return;
        }

        try
        {
            await _endpoints.UpdateUserProfileInfoAsync(new UpdateUserProfileRequest
            {
                Token = _token,
                NewEamil = EditEmail.Trim(),
                newFirstName = EditFirstName.Trim(),
                NewLastName = EditLastName.Trim()
            });

            await _refreshAuthenticatedStateAsync();
            StatusMessage = GetTranslation("Profile_Save_Success");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task ChangeUsernameAsync()
    {
        if (_token == Guid.Empty)
        {
            return;
        }

        try
        {
            await _endpoints.ChangeUsernameAsync(_token, EditUsername.Trim());
            await _refreshAuthenticatedStateAsync();
            StatusMessage = GetTranslation("Profile_Username_Success");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task ChangeMasterPasswordAsync()
    {
        if (_token == Guid.Empty)
        {
            return;
        }

        if (!string.Equals(NewPassword, ConfirmNewPassword, StringComparison.Ordinal))
        {
            StatusMessage = GetTranslation("Validation_RegisterPassword_Mismatch");
            return;
        }

        if (string.IsNullOrWhiteSpace(CurrentPassword) || string.IsNullOrWhiteSpace(NewPassword))
        {
            StatusMessage = GetTranslation("Validation_Password_Required");
            return;
        }

        var currentPasswordHash = SecretTransform.HashPassword(CurrentPassword);
        var newPasswordHash = SecretTransform.HashPassword(NewPassword);

        try
        {
            await _endpoints.ChangeMasterPasswordAsync(new MasterPasswordChangeRequest
            {
                Token = _token,
                Password = currentPasswordHash,
                NewPassword = newPasswordHash
            });

            CurrentPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmNewPassword = string.Empty;
            StatusMessage = GetTranslation("Profile_MasterPassword_Success");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(currentPasswordHash);
            CryptographicOperations.ZeroMemory(newPasswordHash);
        }
    }

    private async Task DeleteAccountAsync()
    {
        if (_token == Guid.Empty)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(DeleteAccountPassword))
        {
            StatusMessage = GetTranslation("Validation_Password_Required");
            return;
        }

        var passwordHash = SecretTransform.HashPassword(DeleteAccountPassword);

        try
        {
            await _endpoints.DeleteUserAccountAsync(_token, passwordHash);
            DeleteAccountPassword = string.Empty;
            await _handleAccountDeletedAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordHash);
        }
    }
}
