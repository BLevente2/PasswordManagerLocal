using PasswordManagerLocal.Helpers;
using PasswordManagerLocal.Services;
using PasswordManagerLocalBackend.Abstractions;
using PasswordManagerLocalBackend.Requests;
using ReactiveUI;
using System.Reactive;

namespace PasswordManagerLocal.ViewModels.Auth;

public sealed class LoginViewModel : ViewModelBase
{
    private readonly IEndpoints _endpoints;
    private readonly Action _navigateToRegistration;
    private readonly Func<Guid, Task> _onAuthenticationSucceededAsync;

    private string _username = string.Empty;
    private string _password = string.Empty;
    private bool _rememberMe;
    private bool _isPasswordVisible;
    private string? _errorMessage;
    private bool _isBusy;

    public LoginViewModel(
        UiPreferencesService uiPreferences,
        IEndpoints endpoints,
        Action navigateToRegistration,
        Func<Guid, Task> onAuthenticationSucceededAsync)
        : base(uiPreferences)
    {
        _endpoints = endpoints;
        _navigateToRegistration = navigateToRegistration;
        _onAuthenticationSucceededAsync = onAuthenticationSucceededAsync;

        LoginCommand = ReactiveCommand.CreateFromTask(LoginAsync);
        NavigateToRegistrationCommand = ReactiveCommand.Create(_navigateToRegistration);
        TogglePasswordVisibilityCommand = ReactiveCommand.Create(TogglePasswordVisibility);
    }

    public string Username
    {
        get => _username;
        set => this.RaiseAndSetIfChanged(ref _username, value);
    }

    public string Password
    {
        get => _password;
        set => this.RaiseAndSetIfChanged(ref _password, value);
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

    public ReactiveCommand<Unit, Unit> LoginCommand { get; }

    public ReactiveCommand<Unit, Unit> NavigateToRegistrationCommand { get; }

    public ReactiveCommand<Unit, Unit> TogglePasswordVisibilityCommand { get; }

    public string Title => GetTranslation("Login_Title");

    public string Subtitle => GetTranslation("Login_Subtitle");

    public string UsernameLabel => GetTranslation("Login_Username_Label");

    public string PasswordLabel => GetTranslation("Login_Password_Label");

    public string RememberMeLabel => GetTranslation("Login_RememberMe_Label");

    public string LoginButtonLabel => GetTranslation("Login_Button");

    public string NavigateToRegisterLabel => GetTranslation("Login_NavigateToRegister_Button");

    public string NoAccountText => GetTranslation("Login_NoAccount_Text");

    public string UsernamePlaceholder => GetTranslation("Login_Username_Placeholder");

    public string PasswordPlaceholder => GetTranslation("Login_Password_Placeholder");

    public string PasswordVisibilityToggleText => GetTranslation(IsPasswordVisible ? "Common_Hide" : "Common_Show");

    protected override void OnLanguageChanged()
    {
        this.RaisePropertyChanged(nameof(Title));
        this.RaisePropertyChanged(nameof(Subtitle));
        this.RaisePropertyChanged(nameof(UsernameLabel));
        this.RaisePropertyChanged(nameof(PasswordLabel));
        this.RaisePropertyChanged(nameof(RememberMeLabel));
        this.RaisePropertyChanged(nameof(LoginButtonLabel));
        this.RaisePropertyChanged(nameof(NavigateToRegisterLabel));
        this.RaisePropertyChanged(nameof(NoAccountText));
        this.RaisePropertyChanged(nameof(UsernamePlaceholder));
        this.RaisePropertyChanged(nameof(PasswordPlaceholder));
        this.RaisePropertyChanged(nameof(PasswordVisibilityToggleText));
    }

    public void Reset()
    {
        Username = string.Empty;
        Password = string.Empty;
        RememberMe = false;
        IsPasswordVisible = false;
        ErrorMessage = null;
        this.RaisePropertyChanged(nameof(HasError));
    }

    private async Task LoginAsync()
    {
        ErrorMessage = null;
        this.RaisePropertyChanged(nameof(HasError));

        if (string.IsNullOrWhiteSpace(Username))
        {
            ErrorMessage = GetTranslation("Validation_Username_Required");
            this.RaisePropertyChanged(nameof(HasError));
            return;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = GetTranslation("Validation_Password_Required");
            this.RaisePropertyChanged(nameof(HasError));
            return;
        }

        var passwordHash = SecretTransform.HashPassword(Password);
        Password = string.Empty;

        try
        {
            IsBusy = true;
            var token = await _endpoints.LoginAsync(new LoginRequest
            {
                Username = Username.Trim(),
                Password = passwordHash,
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
}
