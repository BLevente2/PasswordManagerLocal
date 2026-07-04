using PasswordManagerLocal.Frontend.Helpers;
using PasswordManagerLocal.Frontend.Security;
using PasswordManagerLocal.Frontend.Services;
using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Backend.Requests;
using ReactiveUI;
using System.Reactive;

namespace PasswordManagerLocal.Frontend.ViewModels.Auth;

public sealed class RegistrationViewModel : ViewModelBase
{
    private readonly IEndpoints _endpoints;
    private readonly PasswordStrengthEstimator _passwordStrengthEstimator = new();
    private readonly Action _navigateToLogin;
    private readonly Func<Guid, Task> _onAuthenticationSucceededAsync;
    private Func<Task>? _navigateBackAsync;

    private string _username = string.Empty;
    private string _firstName = string.Empty;
    private string _lastName = string.Empty;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string _confirmPassword = string.Empty;
    private bool _rememberMe;
    private bool _isPasswordVisible;
    private bool _isConfirmPasswordVisible;
    private bool _isBusy;
    private bool _isBackButtonVisible;
    private int _passwordStrength;

    public RegistrationViewModel(
        UiPreferencesService uiPreferences,
        IEndpoints endpoints,
        Action navigateToLogin,
        Func<Guid, Task> onAuthenticationSucceededAsync)
        : base(uiPreferences)
    {
        _endpoints = endpoints;
        _navigateToLogin = navigateToLogin;
        _onAuthenticationSucceededAsync = onAuthenticationSucceededAsync;

        RegisterCommand = ReactiveCommand.CreateFromTask(RegisterAsync);
        NavigateToLoginCommand = ReactiveCommand.Create(_navigateToLogin);
        NavigateBackCommand = ReactiveCommand.CreateFromTask(NavigateBackAsync);
        TogglePasswordVisibilityCommand = ReactiveCommand.Create(TogglePasswordVisibility);
        ToggleConfirmPasswordVisibilityCommand = ReactiveCommand.Create(ToggleConfirmPasswordVisibility);
    }

    public string Username
    {
        get => _username;
        set
        {
            this.RaiseAndSetIfChanged(ref _username, value);
            RefreshPasswordStrength();
        }
    }

    public string FirstName
    {
        get => _firstName;
        set
        {
            this.RaiseAndSetIfChanged(ref _firstName, value);
            RefreshPasswordStrength();
        }
    }

    public string LastName
    {
        get => _lastName;
        set
        {
            this.RaiseAndSetIfChanged(ref _lastName, value);
            RefreshPasswordStrength();
        }
    }

    public string Email
    {
        get => _email;
        set
        {
            this.RaiseAndSetIfChanged(ref _email, value);
            RefreshPasswordStrength();
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            this.RaiseAndSetIfChanged(ref _password, value);
            RefreshPasswordStrength();
        }
    }

    public string ConfirmPassword
    {
        get => _confirmPassword;
        set => this.RaiseAndSetIfChanged(ref _confirmPassword, value);
    }

    public bool RememberMe
    {
        get => _rememberMe;
        set => this.RaiseAndSetIfChanged(ref _rememberMe, value);
    }

    public bool IsPasswordVisible
    {
        get => _isPasswordVisible;
        set
        {
            this.RaiseAndSetIfChanged(ref _isPasswordVisible, value);
            this.RaisePropertyChanged(nameof(PasswordMaskCharacter));
            this.RaisePropertyChanged(nameof(PasswordVisibilityToggleText));
        }
    }

    public bool IsConfirmPasswordVisible
    {
        get => _isConfirmPasswordVisible;
        set
        {
            this.RaiseAndSetIfChanged(ref _isConfirmPasswordVisible, value);
            this.RaisePropertyChanged(nameof(ConfirmPasswordMaskCharacter));
            this.RaisePropertyChanged(nameof(ConfirmPasswordVisibilityToggleText));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public bool IsBackButtonVisible
    {
        get => _isBackButtonVisible;
        private set => this.RaiseAndSetIfChanged(ref _isBackButtonVisible, value);
    }

    public char PasswordMaskCharacter => IsPasswordVisible ? '\0' : '●';

    public char ConfirmPasswordMaskCharacter => IsConfirmPasswordVisible ? '\0' : '●';

    public int PasswordStrength => _passwordStrength;

    public ReactiveCommand<Unit, Unit> RegisterCommand { get; }

    public ReactiveCommand<Unit, Unit> NavigateToLoginCommand { get; }

    public ReactiveCommand<Unit, Unit> NavigateBackCommand { get; }

    public ReactiveCommand<Unit, Unit> TogglePasswordVisibilityCommand { get; }

    public ReactiveCommand<Unit, Unit> ToggleConfirmPasswordVisibilityCommand { get; }

    public string Title => GetTranslation("Register_Title");

    public string BackLabel => GetTranslation("Common_Back");

    public string Subtitle => GetTranslation("Register_Subtitle");

    public string UsernameLabel => GetTranslation("Register_Username_Label");

    public string FirstNameLabel => GetTranslation("Register_FirstName_Label");

    public string LastNameLabel => GetTranslation("Register_LastName_Label");

    public string EmailLabel => GetTranslation("Register_Email_Label");

    public string PasswordLabel => GetTranslation("Register_Password_Label");

    public string ConfirmPasswordLabel => GetTranslation("Register_ConfirmPassword_Label");

    public string RememberMeLabel => GetTranslation("Register_RememberMe_Label");

    public string RegisterButtonLabel => GetTranslation("Register_Button");

    public string AlreadyHaveAccountText => GetTranslation("Register_AlreadyHaveAccount_Text");

    public string NavigateToLoginLabel => GetTranslation("Register_NavigateToLogin_Button");

    public string UsernamePlaceholder => GetTranslation("Register_Username_Placeholder");

    public string FirstNamePlaceholder => GetTranslation("Register_FirstName_Placeholder");

    public string LastNamePlaceholder => GetTranslation("Register_LastName_Placeholder");

    public string EmailPlaceholder => GetTranslation("Register_Email_Placeholder");

    public string PasswordPlaceholder => GetTranslation("Register_Password_Placeholder");

    public string ConfirmPasswordPlaceholder => GetTranslation("Register_ConfirmPassword_Placeholder");

    public string PasswordVisibilityToggleText => GetTranslation(IsPasswordVisible ? "Common_Hide" : "Common_Show");

    public string ConfirmPasswordVisibilityToggleText => GetTranslation(IsConfirmPasswordVisible ? "Common_Hide" : "Common_Show");

    public string BusyText => GetTranslation("Common_Loading");

    public string PasswordStrengthLabel => GetTranslation("PasswordStrength_Label");

    public string PasswordStrengthInfoTitle => GetTranslation("PasswordStrength_Info_Title");

    public string PasswordStrengthInfoBody => GetTranslation("PasswordStrength_Info_Body");

    public string PasswordStrengthInfoAccessibleLabel => GetTranslation("PasswordStrength_Info_AccessibleLabel");

    protected override void OnLanguageChanged()
    {
        this.RaisePropertyChanged(nameof(Title));
        this.RaisePropertyChanged(nameof(BackLabel));
        this.RaisePropertyChanged(nameof(Subtitle));
        this.RaisePropertyChanged(nameof(UsernameLabel));
        this.RaisePropertyChanged(nameof(FirstNameLabel));
        this.RaisePropertyChanged(nameof(LastNameLabel));
        this.RaisePropertyChanged(nameof(EmailLabel));
        this.RaisePropertyChanged(nameof(PasswordLabel));
        this.RaisePropertyChanged(nameof(ConfirmPasswordLabel));
        this.RaisePropertyChanged(nameof(RememberMeLabel));
        this.RaisePropertyChanged(nameof(RegisterButtonLabel));
        this.RaisePropertyChanged(nameof(AlreadyHaveAccountText));
        this.RaisePropertyChanged(nameof(NavigateToLoginLabel));
        this.RaisePropertyChanged(nameof(UsernamePlaceholder));
        this.RaisePropertyChanged(nameof(FirstNamePlaceholder));
        this.RaisePropertyChanged(nameof(LastNamePlaceholder));
        this.RaisePropertyChanged(nameof(EmailPlaceholder));
        this.RaisePropertyChanged(nameof(PasswordPlaceholder));
        this.RaisePropertyChanged(nameof(ConfirmPasswordPlaceholder));
        this.RaisePropertyChanged(nameof(PasswordVisibilityToggleText));
        this.RaisePropertyChanged(nameof(ConfirmPasswordVisibilityToggleText));
        this.RaisePropertyChanged(nameof(BusyText));
        this.RaisePropertyChanged(nameof(PasswordStrengthLabel));
        this.RaisePropertyChanged(nameof(PasswordStrengthInfoTitle));
        this.RaisePropertyChanged(nameof(PasswordStrengthInfoBody));
        this.RaisePropertyChanged(nameof(PasswordStrengthInfoAccessibleLabel));
    }

    public void Reset()
    {
        Username = string.Empty;
        FirstName = string.Empty;
        LastName = string.Empty;
        Email = string.Empty;
        Password = string.Empty;
        ConfirmPassword = string.Empty;
        RememberMe = false;
        IsPasswordVisible = false;
        IsConfirmPasswordVisible = false;
        ClearStatusMessage();
    }


    public void SetBackNavigation(bool isVisible, Func<Task>? navigateBackAsync)
    {
        IsBackButtonVisible = isVisible;
        _navigateBackAsync = navigateBackAsync;
    }


    private async Task NavigateBackAsync()
    {
        if (IsBusy || _navigateBackAsync is null)
            return;

        await _navigateBackAsync();
    }

    private async Task RegisterAsync()
    {
        if (IsBusy || !ValidateRegistrationInput())
            return;

        var passwordHash = SecretTransform.HashPassword(Password);
        Password = string.Empty;
        ConfirmPassword = string.Empty;

        try
        {
            IsBusy = true;
            var token = await _endpoints.RegisterAsync(CreateRegistrationRequest(passwordHash));
            await _onAuthenticationSucceededAsync(token);
            Reset();
        }
        catch (Exception ex)
        {
            ShowErrorMessage(GetSafeErrorMessage(ex));
        }
        finally
        {
            IsBusy = false;
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(passwordHash);
        }
    }

    private bool ValidateRegistrationInput()
    {
        ClearStatusMessage();
        var validationError = GetRegistrationValidationError();
        if (validationError is null)
            return true;

        ShowErrorMessage(validationError);
        return false;
    }

    private string? GetRegistrationValidationError()
    {
        if (string.IsNullOrWhiteSpace(Username))
            return GetTranslation("Validation_Username_Required");
        if (string.IsNullOrWhiteSpace(FirstName))
            return GetTranslation("Validation_FirstName_Required");
        if (string.IsNullOrWhiteSpace(LastName))
            return GetTranslation("Validation_LastName_Required");
        if (string.IsNullOrWhiteSpace(Email))
            return GetTranslation("Validation_Email_Required");
        if (string.IsNullOrWhiteSpace(Password))
            return GetTranslation("Validation_RegisterPassword_Required");
        if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
            return GetTranslation("Validation_RegisterPassword_Mismatch");

        return null;
    }

    private void RefreshPasswordStrength()
    {
        var result = _passwordStrengthEstimator.Evaluate(
            Password,
            [Username, FirstName, LastName, Email]);

        if (_passwordStrength == result.Score)
            return;

        _passwordStrength = result.Score;
        this.RaisePropertyChanged(nameof(PasswordStrength));
    }

    private RegistrationRequest CreateRegistrationRequest(byte[] passwordHash) =>
        new()
        {
            Username = Username.Trim(),
            Password = passwordHash,
            FirstName = FirstName.Trim(),
            LastName = LastName.Trim(),
            Email = Email.Trim(),
            RememberMe = RememberMe
        };

    private void TogglePasswordVisibility() => IsPasswordVisible = !IsPasswordVisible;

    private void ToggleConfirmPasswordVisibility() => IsConfirmPasswordVisible = !IsConfirmPasswordVisible;
}
