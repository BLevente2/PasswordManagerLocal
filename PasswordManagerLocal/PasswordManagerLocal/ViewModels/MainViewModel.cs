using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ReactiveUI;
using System;
using System.Reactive;

namespace PasswordManagerLocal.ViewModels;

public class MainViewModel : ViewModelBase
{
    private AuthViewMode _authMode;

    private string _username = string.Empty;
    private string _password = string.Empty;
    private bool _rememberMe;

    private string? _usernameErrorMessage;
    private string? _passwordErrorMessage;

    private bool _isPasswordVisible;

    private string _registerUsername = string.Empty;
    private string _registerFirstName = string.Empty;
    private string _registerLastName = string.Empty;
    private string _registerEmail = string.Empty;
    private string _registerPassword = string.Empty;
    private string _registerConfirmPassword = string.Empty;
    private bool _registerRememberMe;

    private string? _registerUsernameErrorMessage;
    private string? _registerFirstNameErrorMessage;
    private string? _registerLastNameErrorMessage;
    private string? _registerEmailErrorMessage;
    private string? _registerPasswordErrorMessage;
    private string? _registerConfirmPasswordErrorMessage;

    private bool _isRegisterPasswordVisible;
    private bool _isRegisterConfirmPasswordVisible;

    private IImage? _passwordToggleIcon;
    private IImage? _registerPasswordToggleIcon;
    private IImage? _registerConfirmPasswordToggleIcon;

    private static readonly Uri EyeShowIconUri =
        new("avares://PasswordManagerLocal/Assets/Icons/eye_show.png");

    private static readonly Uri EyeHideIconUri =
        new("avares://PasswordManagerLocal/Assets/Icons/eye_hide.png");

    private static IImage? _eyeShowImage;
    private static IImage? _eyeHideImage;

    public MainViewModel()
    {
        _authMode = AuthViewMode.Login;

        TogglePasswordVisibilityCommand = ReactiveCommand.Create<string>(TogglePasswordVisibility);
        LoginCommand = ReactiveCommand.Create(ExecuteLogin);
        RegisterCommand = ReactiveCommand.Create(ExecuteRegister);
        NavigateToRegisterCommand = ReactiveCommand.Create(ExecuteNavigateToRegister);
        NavigateToLoginWithExistingAccountCommand =
            ReactiveCommand.Create(ExecuteNavigateToLoginWithExistingAccount);

        UpdatePasswordToggleIcons();
    }

    public AuthViewMode AuthMode
    {
        get => _authMode;
        private set
        {
            if (value == _authMode)
            {
                return;
            }

            _authMode = value;
            this.RaisePropertyChanged(nameof(AuthMode));
            this.RaisePropertyChanged(nameof(IsLoginMode));
            this.RaisePropertyChanged(nameof(IsRegisterMode));
        }
    }

    public bool IsLoginMode => AuthMode == AuthViewMode.Login;

    public bool IsRegisterMode => AuthMode == AuthViewMode.Register;

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

    public string? UsernameErrorMessage
    {
        get => _usernameErrorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _usernameErrorMessage, value);
            this.RaisePropertyChanged(nameof(HasUsernameError));
        }
    }

    public string? PasswordErrorMessage
    {
        get => _passwordErrorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _passwordErrorMessage, value);
            this.RaisePropertyChanged(nameof(HasPasswordError));
        }
    }

    public bool HasUsernameError => !string.IsNullOrEmpty(UsernameErrorMessage);

    public bool HasPasswordError => !string.IsNullOrEmpty(PasswordErrorMessage);

    public string RegisterUsername
    {
        get => _registerUsername;
        set => this.RaiseAndSetIfChanged(ref _registerUsername, value);
    }

    public string RegisterFirstName
    {
        get => _registerFirstName;
        set => this.RaiseAndSetIfChanged(ref _registerFirstName, value);
    }

    public string RegisterLastName
    {
        get => _registerLastName;
        set => this.RaiseAndSetIfChanged(ref _registerLastName, value);
    }

    public string RegisterEmail
    {
        get => _registerEmail;
        set => this.RaiseAndSetIfChanged(ref _registerEmail, value);
    }

    public string RegisterPassword
    {
        get => _registerPassword;
        set => this.RaiseAndSetIfChanged(ref _registerPassword, value);
    }

    public string RegisterConfirmPassword
    {
        get => _registerConfirmPassword;
        set => this.RaiseAndSetIfChanged(ref _registerConfirmPassword, value);
    }

    public bool RegisterRememberMe
    {
        get => _registerRememberMe;
        set => this.RaiseAndSetIfChanged(ref _registerRememberMe, value);
    }

    public string? RegisterUsernameErrorMessage
    {
        get => _registerUsernameErrorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _registerUsernameErrorMessage, value);
            this.RaisePropertyChanged(nameof(HasRegisterUsernameError));
        }
    }

    public string? RegisterFirstNameErrorMessage
    {
        get => _registerFirstNameErrorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _registerFirstNameErrorMessage, value);
            this.RaisePropertyChanged(nameof(HasRegisterFirstNameError));
        }
    }

    public string? RegisterLastNameErrorMessage
    {
        get => _registerLastNameErrorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _registerLastNameErrorMessage, value);
            this.RaisePropertyChanged(nameof(HasRegisterLastNameError));
        }
    }

    public string? RegisterEmailErrorMessage
    {
        get => _registerEmailErrorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _registerEmailErrorMessage, value);
            this.RaisePropertyChanged(nameof(HasRegisterEmailError));
        }
    }

    public string? RegisterPasswordErrorMessage
    {
        get => _registerPasswordErrorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _registerPasswordErrorMessage, value);
            this.RaisePropertyChanged(nameof(HasRegisterPasswordError));
        }
    }

    public string? RegisterConfirmPasswordErrorMessage
    {
        get => _registerConfirmPasswordErrorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _registerConfirmPasswordErrorMessage, value);
            this.RaisePropertyChanged(nameof(HasRegisterConfirmPasswordError));
        }
    }

    public bool HasRegisterUsernameError => !string.IsNullOrEmpty(RegisterUsernameErrorMessage);

    public bool HasRegisterFirstNameError => !string.IsNullOrEmpty(RegisterFirstNameErrorMessage);

    public bool HasRegisterLastNameError => !string.IsNullOrEmpty(RegisterLastNameErrorMessage);

    public bool HasRegisterEmailError => !string.IsNullOrEmpty(RegisterEmailErrorMessage);

    public bool HasRegisterPasswordError => !string.IsNullOrEmpty(RegisterPasswordErrorMessage);

    public bool HasRegisterConfirmPasswordError => !string.IsNullOrEmpty(RegisterConfirmPasswordErrorMessage);

    public bool IsPasswordVisible
    {
        get => _isPasswordVisible;
        private set
        {
            if (value == _isPasswordVisible)
            {
                return;
            }

            _isPasswordVisible = value;
            this.RaisePropertyChanged(nameof(IsPasswordVisible));
            this.RaisePropertyChanged(nameof(PasswordMaskCharacter));
            UpdatePasswordToggleIcons();
        }
    }

    public bool IsRegisterPasswordVisible
    {
        get => _isRegisterPasswordVisible;
        private set
        {
            if (value == _isRegisterPasswordVisible)
            {
                return;
            }

            _isRegisterPasswordVisible = value;
            this.RaisePropertyChanged(nameof(IsRegisterPasswordVisible));
            this.RaisePropertyChanged(nameof(RegisterPasswordMaskCharacter));
            UpdatePasswordToggleIcons();
        }
    }

    public bool IsRegisterConfirmPasswordVisible
    {
        get => _isRegisterConfirmPasswordVisible;
        private set
        {
            if (value == _isRegisterConfirmPasswordVisible)
            {
                return;
            }

            _isRegisterConfirmPasswordVisible = value;
            this.RaisePropertyChanged(nameof(IsRegisterConfirmPasswordVisible));
            this.RaisePropertyChanged(nameof(RegisterConfirmPasswordMaskCharacter));
            UpdatePasswordToggleIcons();
        }
    }

    public char PasswordMaskCharacter => IsPasswordVisible ? '\0' : '●';

    public char RegisterPasswordMaskCharacter => IsRegisterPasswordVisible ? '\0' : '●';

    public char RegisterConfirmPasswordMaskCharacter => IsRegisterConfirmPasswordVisible ? '\0' : '●';

    public IImage? PasswordToggleIcon
    {
        get => _passwordToggleIcon;
        private set => this.RaiseAndSetIfChanged(ref _passwordToggleIcon, value);
    }

    public IImage? RegisterPasswordToggleIcon
    {
        get => _registerPasswordToggleIcon;
        private set => this.RaiseAndSetIfChanged(ref _registerPasswordToggleIcon, value);
    }

    public IImage? RegisterConfirmPasswordToggleIcon
    {
        get => _registerConfirmPasswordToggleIcon;
        private set => this.RaiseAndSetIfChanged(ref _registerConfirmPasswordToggleIcon, value);
    }

    public ReactiveCommand<string, Unit> TogglePasswordVisibilityCommand { get; }

    public ReactiveCommand<Unit, Unit> LoginCommand { get; }

    public ReactiveCommand<Unit, Unit> RegisterCommand { get; }

    public ReactiveCommand<Unit, Unit> NavigateToRegisterCommand { get; }

    public ReactiveCommand<Unit, Unit> NavigateToLoginWithExistingAccountCommand { get; }

    public string LoginTitle => GetTranslation("Login_Title");

    public string LoginUsernameLabel => GetTranslation("Login_Username_Label");

    public string LoginPasswordLabel => GetTranslation("Login_Password_Label");

    public string LoginRememberMeLabel => GetTranslation("Login_RememberMe_Label");

    public string LoginButtonLabel => GetTranslation("Login_Button");

    public string LoginNoAccountText => GetTranslation("Login_NoAccount_Text");

    public string LoginExistingOnDeviceText => GetTranslation("Login_ExistingOnDevice_Text");

    public string LoginExistingOtherDeviceText => GetTranslation("Login_ExistingOtherDevice_Text");

    public string LoginNavigateToRegisterButtonLabel =>
        GetTranslation("Login_NavigateToRegister_Button");

    public string LoginUsernamePlaceholder => GetTranslation("Login_Username_Placeholder");

    public string LoginPasswordPlaceholder => GetTranslation("Login_Password_Placeholder");

    public string UsernameRequiredMessage => GetTranslation("Validation_Username_Required");

    public string PasswordRequiredMessage => GetTranslation("Validation_Password_Required");

    public string RegisterTitle => GetTranslation("Register_Title");

    public string RegisterUsernameLabel => GetTranslation("Register_Username_Label");

    public string RegisterFirstNameLabel => GetTranslation("Register_FirstName_Label");

    public string RegisterLastNameLabel => GetTranslation("Register_LastName_Label");

    public string RegisterEmailLabel => GetTranslation("Register_Email_Label");

    public string RegisterPasswordLabel => GetTranslation("Register_Password_Label");

    public string RegisterConfirmPasswordLabel => GetTranslation("Register_ConfirmPassword_Label");

    public string RegisterRememberMeLabel => GetTranslation("Register_RememberMe_Label");

    public string RegisterButtonLabel => GetTranslation("Register_Button");

    public string RegisterAlreadyHaveAccountText => GetTranslation("Register_AlreadyHaveAccount_Text");

    public string RegisterExistingOtherDeviceText => GetTranslation("Register_ExistingOtherDevice_Text");

    public string RegisterUsernamePlaceholder => GetTranslation("Register_Username_Placeholder");

    public string RegisterFirstNamePlaceholder => GetTranslation("Register_FirstName_Placeholder");

    public string RegisterLastNamePlaceholder => GetTranslation("Register_LastName_Placeholder");

    public string RegisterEmailPlaceholder => GetTranslation("Register_Email_Placeholder");

    public string RegisterPasswordPlaceholder => GetTranslation("Register_Password_Placeholder");

    public string RegisterConfirmPasswordPlaceholder => GetTranslation("Register_ConfirmPassword_Placeholder");

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();

        RaiseAuthTranslationPropertiesChanged();

        if (!string.IsNullOrEmpty(_usernameErrorMessage))
        {
            UsernameErrorMessage = UsernameRequiredMessage;
        }

        if (!string.IsNullOrEmpty(_passwordErrorMessage))
        {
            PasswordErrorMessage = PasswordRequiredMessage;
        }

        // Ha szeretnéd, itt hasonlóan újra be lehet állítani
        // a regisztrációs hibákat is az új nyelvre.
    }

    private void TogglePasswordVisibility(string target)
    {
        switch (target)
        {
            case "LoginPassword":
                IsPasswordVisible = !IsPasswordVisible;
                break;
            case "RegisterPassword":
                IsRegisterPasswordVisible = !IsRegisterPasswordVisible;
                break;
            case "RegisterConfirmPassword":
                IsRegisterConfirmPasswordVisible = !IsRegisterConfirmPasswordVisible;
                break;
        }
    }

    private void ExecuteLogin()
    {
        ClearLoginErrors();

        if (string.IsNullOrWhiteSpace(Username))
        {
            UsernameErrorMessage = UsernameRequiredMessage;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            PasswordErrorMessage = PasswordRequiredMessage;
        }
    }

    private void ExecuteRegister()
    {
        ClearRegistrationErrors();

        var hasError = false;

        if (string.IsNullOrWhiteSpace(RegisterUsername))
        {
            RegisterUsernameErrorMessage = GetTranslation("Validation_Username_Required");
            hasError = true;
        }

        if (string.IsNullOrWhiteSpace(RegisterFirstName))
        {
            RegisterFirstNameErrorMessage = GetTranslation("Validation_FirstName_Required");
            hasError = true;
        }

        if (string.IsNullOrWhiteSpace(RegisterLastName))
        {
            RegisterLastNameErrorMessage = GetTranslation("Validation_LastName_Required");
            hasError = true;
        }

        if (string.IsNullOrWhiteSpace(RegisterEmail))
        {
            RegisterEmailErrorMessage = GetTranslation("Validation_Email_Required");
            hasError = true;
        }
        else if (!RegisterEmail.Contains('@', StringComparison.Ordinal))
        {
            RegisterEmailErrorMessage = GetTranslation("Validation_Email_Invalid");
            hasError = true;
        }

        if (string.IsNullOrWhiteSpace(RegisterPassword))
        {
            RegisterPasswordErrorMessage = GetTranslation("Validation_RegisterPassword_Required");
            hasError = true;
        }

        if (string.IsNullOrWhiteSpace(RegisterConfirmPassword))
        {
            RegisterConfirmPasswordErrorMessage = GetTranslation("Validation_RegisterConfirmPassword_Required");
            hasError = true;
        }

        if (!string.IsNullOrWhiteSpace(RegisterPassword) &&
            !string.IsNullOrWhiteSpace(RegisterConfirmPassword) &&
            !string.Equals(RegisterPassword, RegisterConfirmPassword, StringComparison.Ordinal))
        {
            RegisterConfirmPasswordErrorMessage = GetTranslation("Validation_RegisterPassword_Mismatch");
            hasError = true;
        }

        if (hasError)
        {
            return;
        }

    }

    private void ExecuteNavigateToRegister()
    {
        ClearLoginErrors();
        ClearRegistrationErrors();
        AuthMode = AuthViewMode.Register;
    }

    private void ExecuteNavigateToLoginWithExistingAccount()
    {
        ClearLoginErrors();
        ClearRegistrationErrors();
        AuthMode = AuthViewMode.Login;
    }

    private void ClearLoginErrors()
    {
        UsernameErrorMessage = null;
        PasswordErrorMessage = null;
    }

    private void ClearRegistrationErrors()
    {
        RegisterUsernameErrorMessage = null;
        RegisterFirstNameErrorMessage = null;
        RegisterLastNameErrorMessage = null;
        RegisterEmailErrorMessage = null;
        RegisterPasswordErrorMessage = null;
        RegisterConfirmPasswordErrorMessage = null;
    }

    private void RaiseAuthTranslationPropertiesChanged()
    {
        this.RaisePropertyChanged(nameof(LoginTitle));
        this.RaisePropertyChanged(nameof(LoginUsernameLabel));
        this.RaisePropertyChanged(nameof(LoginPasswordLabel));
        this.RaisePropertyChanged(nameof(LoginRememberMeLabel));
        this.RaisePropertyChanged(nameof(LoginButtonLabel));
        this.RaisePropertyChanged(nameof(LoginNoAccountText));
        this.RaisePropertyChanged(nameof(LoginExistingOnDeviceText));
        this.RaisePropertyChanged(nameof(LoginExistingOtherDeviceText));
        this.RaisePropertyChanged(nameof(LoginNavigateToRegisterButtonLabel));
        this.RaisePropertyChanged(nameof(LoginUsernamePlaceholder));
        this.RaisePropertyChanged(nameof(LoginPasswordPlaceholder));
        this.RaisePropertyChanged(nameof(UsernameRequiredMessage));
        this.RaisePropertyChanged(nameof(PasswordRequiredMessage));

        this.RaisePropertyChanged(nameof(RegisterTitle));
        this.RaisePropertyChanged(nameof(RegisterUsernameLabel));
        this.RaisePropertyChanged(nameof(RegisterFirstNameLabel));
        this.RaisePropertyChanged(nameof(RegisterLastNameLabel));
        this.RaisePropertyChanged(nameof(RegisterEmailLabel));
        this.RaisePropertyChanged(nameof(RegisterPasswordLabel));
        this.RaisePropertyChanged(nameof(RegisterConfirmPasswordLabel));
        this.RaisePropertyChanged(nameof(RegisterRememberMeLabel));
        this.RaisePropertyChanged(nameof(RegisterButtonLabel));
        this.RaisePropertyChanged(nameof(RegisterAlreadyHaveAccountText));
        this.RaisePropertyChanged(nameof(RegisterExistingOtherDeviceText));
        this.RaisePropertyChanged(nameof(RegisterUsernamePlaceholder));
        this.RaisePropertyChanged(nameof(RegisterFirstNamePlaceholder));
        this.RaisePropertyChanged(nameof(RegisterLastNamePlaceholder));
        this.RaisePropertyChanged(nameof(RegisterEmailPlaceholder));
        this.RaisePropertyChanged(nameof(RegisterPasswordPlaceholder));
        this.RaisePropertyChanged(nameof(RegisterConfirmPasswordPlaceholder));
    }

    private void EnsurePasswordIconAssetsLoaded()
    {
        if (_eyeShowImage != null && _eyeHideImage != null)
        {
            return;
        }

        try
        {
            if (_eyeShowImage == null && AssetLoader.Exists(EyeShowIconUri))
            {
                using var stream = AssetLoader.Open(EyeShowIconUri);
                _eyeShowImage = new Bitmap(stream);
            }

            if (_eyeHideImage == null && AssetLoader.Exists(EyeHideIconUri))
            {
                using var stream = AssetLoader.Open(EyeHideIconUri);
                _eyeHideImage = new Bitmap(stream);
            }
        }
        catch
        {
        }
    }

    private void UpdatePasswordToggleIcons()
    {
        EnsurePasswordIconAssetsLoaded();

        PasswordToggleIcon = IsPasswordVisible ? _eyeHideImage : _eyeShowImage;
        RegisterPasswordToggleIcon = IsRegisterPasswordVisible ? _eyeHideImage : _eyeShowImage;
        RegisterConfirmPasswordToggleIcon = IsRegisterConfirmPasswordVisible ? _eyeHideImage : _eyeShowImage;
    }
}