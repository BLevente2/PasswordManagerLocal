using PasswordManagerLocal.Helpers;
using PasswordManagerLocal.Services;
using PasswordManagerLocalBackend.Abstractions;
using PasswordManagerLocalBackend.Requests;
using ReactiveUI;
using System.Reactive;

namespace PasswordManagerLocal.ViewModels.Auth;

public sealed class RegistrationViewModel : ViewModelBase
{
    private readonly IEndpoints _endpoints;
    private readonly Action _navigateToLogin;
    private readonly Func<Guid, Task> _onAuthenticationSucceededAsync;

    private string _username = string.Empty;
    private string _firstName = string.Empty;
    private string _lastName = string.Empty;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string _confirmPassword = string.Empty;
    private bool _rememberMe;
    private bool _isPasswordVisible;
    private bool _isConfirmPasswordVisible;
    private string? _errorMessage;
    private bool _isBusy;

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
        TogglePasswordVisibilityCommand = ReactiveCommand.Create(TogglePasswordVisibility);
        ToggleConfirmPasswordVisibilityCommand = ReactiveCommand.Create(ToggleConfirmPasswordVisibility);
    }

    public string Username
    {
        get => _username;
        set => this.RaiseAndSetIfChanged(ref _username, value);
    }

    public string FirstName
    {
        get => _firstName;
        set => this.RaiseAndSetIfChanged(ref _firstName, value);
    }

    public string LastName
    {
        get => _lastName;
        set => this.RaiseAndSetIfChanged(ref _lastName, value);
    }

    public string Email
    {
        get => _email;
        set => this.RaiseAndSetIfChanged(ref _email, value);
    }

    public string Password
    {
        get => _password;
        set => this.RaiseAndSetIfChanged(ref _password, value);
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

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public char PasswordMaskCharacter => IsPasswordVisible ? '\0' : '●';

    public char ConfirmPasswordMaskCharacter => IsConfirmPasswordVisible ? '\0' : '●';

    public ReactiveCommand<Unit, Unit> RegisterCommand { get; }

    public ReactiveCommand<Unit, Unit> NavigateToLoginCommand { get; }

    public ReactiveCommand<Unit, Unit> TogglePasswordVisibilityCommand { get; }

    public ReactiveCommand<Unit, Unit> ToggleConfirmPasswordVisibilityCommand { get; }

    public string Title => GetTranslation("Register_Title");

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

    protected override void OnLanguageChanged()
    {
        this.RaisePropertyChanged(nameof(Title));
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
        ErrorMessage = null;
        this.RaisePropertyChanged(nameof(HasError));
    }

    private async Task RegisterAsync()
    {
        ErrorMessage = null;
        this.RaisePropertyChanged(nameof(HasError));

        if (string.IsNullOrWhiteSpace(Username))
        {
            ErrorMessage = GetTranslation("Validation_Username_Required");
            this.RaisePropertyChanged(nameof(HasError));
            return;
        }

        if (string.IsNullOrWhiteSpace(FirstName))
        {
            ErrorMessage = GetTranslation("Validation_FirstName_Required");
            this.RaisePropertyChanged(nameof(HasError));
            return;
        }

        if (string.IsNullOrWhiteSpace(LastName))
        {
            ErrorMessage = GetTranslation("Validation_LastName_Required");
            this.RaisePropertyChanged(nameof(HasError));
            return;
        }

        if (string.IsNullOrWhiteSpace(Email))
        {
            ErrorMessage = GetTranslation("Validation_Email_Required");
            this.RaisePropertyChanged(nameof(HasError));
            return;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = GetTranslation("Validation_RegisterPassword_Required");
            this.RaisePropertyChanged(nameof(HasError));
            return;
        }

        if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
        {
            ErrorMessage = GetTranslation("Validation_RegisterPassword_Mismatch");
            this.RaisePropertyChanged(nameof(HasError));
            return;
        }

        var passwordHash = SecretTransform.HashPassword(Password);
        Password = string.Empty;
        ConfirmPassword = string.Empty;

        try
        {
            IsBusy = true;
            var token = await _endpoints.RegisterAsync(new RegistrationRequest
            {
                Username = Username.Trim(),
                Password = passwordHash,
                FirstName = FirstName.Trim(),
                LastName = LastName.Trim(),
                Email = Email.Trim(),
                RememberMe = RememberMe
            });

            await _onAuthenticationSucceededAsync(token);
            Reset();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            this.RaisePropertyChanged(nameof(HasError));
        }
        finally
        {
            IsBusy = false;
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(passwordHash);
        }
    }

    private void TogglePasswordVisibility() => IsPasswordVisible = !IsPasswordVisible;

    private void ToggleConfirmPasswordVisibility() => IsConfirmPasswordVisible = !IsConfirmPasswordVisible;
}
