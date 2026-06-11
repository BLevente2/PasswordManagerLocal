using Avalonia.Threading;
using PasswordManagerLocal.Abstractions.Services;
using PasswordManagerLocal.Localization;
using PasswordManagerLocal.Services;
using PasswordManagerLocal.ViewModels.Auth;
using PasswordManagerLocal.ViewModels.Pages;
using PasswordManagerLocalBackend.Abstractions;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Responses;
using ReactiveUI;
using System.Linq;
using System.Reactive;

namespace PasswordManagerLocal.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly IEndpoints _endpoints;
    private readonly IAuthSessionRegistry _authSessionRegistry;

    private ViewModelBase _currentPageViewModel;
    private bool _isAuthenticated;
    private string _currentUserDisplayName = string.Empty;
    private string _currentUserSubtitle = string.Empty;
    private string? _statusMessage;
    private DispatcherTimer? _sessionMonitorTimer;
    private bool _isCheckingSession;

    public MainViewModel(IEndpoints endpoints)
        : this(endpoints, App.AuthSessionRegistry, new UiPreferencesService())
    {
    }

    private MainViewModel(IEndpoints endpoints, IAuthSessionRegistry authSessionRegistry, UiPreferencesService uiPreferences)
        : base(uiPreferences)
    {
        _endpoints = endpoints;
        _authSessionRegistry = authSessionRegistry;

        LoginViewModel = new LoginViewModel(uiPreferences, _endpoints, NavigateToRegistration, OnAuthenticationSucceededAsync);
        RegistrationViewModel = new RegistrationViewModel(uiPreferences, _endpoints, NavigateToLogin, OnAuthenticationSucceededAsync);
        PasswordsViewModel = new PasswordsViewModel(uiPreferences, _endpoints);
        ProfileViewModel = new ProfileViewModel(uiPreferences, _endpoints, RefreshProfileDataAsync, HandleAccountDeletedAsync);

        _currentPageViewModel = LoginViewModel;

        SetHungarianLanguageCommand = ReactiveCommand.Create(() => { UiPreferences.CurrentLanguage = AppLanguage.Hungarian; });
        SetEnglishLanguageCommand = ReactiveCommand.Create(() => { UiPreferences.CurrentLanguage = AppLanguage.English; });
        SetLightThemeCommand = ReactiveCommand.Create(() => { UiPreferences.CurrentThemeMode = AppThemeMode.Light; });
        SetDarkThemeCommand = ReactiveCommand.Create(() => { UiPreferences.CurrentThemeMode = AppThemeMode.Dark; });
        ShowPasswordsCommand = ReactiveCommand.Create(NavigateToPasswords);
        ShowProfileCommand = ReactiveCommand.Create(NavigateToProfile);
        ShowDevicesCommand = ReactiveCommand.Create(NavigateToDevices);
        ShowLoginCommand = ReactiveCommand.Create(NavigateToLogin);
        ShowRegistrationCommand = ReactiveCommand.Create(NavigateToRegistration);
        LogoutCommand = ReactiveCommand.CreateFromTask(LogoutAsync);
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAuthenticatedStateAsync);
        RefreshVisiblePageCommand = ReactiveCommand.CreateFromTask(RefreshVisiblePageAsync);
    }

    public LoginViewModel LoginViewModel { get; }

    public RegistrationViewModel RegistrationViewModel { get; }

    public PasswordsViewModel PasswordsViewModel { get; }

    public ProfileViewModel ProfileViewModel { get; }

    public ViewModelBase CurrentPageViewModel
    {
        get => _currentPageViewModel;
        private set => this.RaiseAndSetIfChanged(ref _currentPageViewModel, value);
    }

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isAuthenticated, value);
            this.RaisePropertyChanged(nameof(IsAnonymous));
            this.RaisePropertyChanged(nameof(HeaderSubtitle));
            this.RaisePropertyChanged(nameof(HasHeaderSubtitle));
        }
    }

    public bool IsAnonymous => !IsAuthenticated;

    public string CurrentUserDisplayName
    {
        get => _currentUserDisplayName;
        private set => this.RaiseAndSetIfChanged(ref _currentUserDisplayName, value);
    }

    public string CurrentUserSubtitle
    {
        get => _currentUserSubtitle;
        private set
        {
            this.RaiseAndSetIfChanged(ref _currentUserSubtitle, value);
            this.RaisePropertyChanged(nameof(HeaderSubtitle));
            this.RaisePropertyChanged(nameof(HasHeaderSubtitle));
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _statusMessage, value);
            this.RaisePropertyChanged(nameof(HasStatusMessage));
            this.RaisePropertyChanged(nameof(HeaderSubtitle));
            this.RaisePropertyChanged(nameof(HasHeaderSubtitle));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public ReactiveCommand<Unit, Unit> SetHungarianLanguageCommand { get; }

    public ReactiveCommand<Unit, Unit> SetEnglishLanguageCommand { get; }

    public ReactiveCommand<Unit, Unit> SetLightThemeCommand { get; }

    public ReactiveCommand<Unit, Unit> SetDarkThemeCommand { get; }

    public ReactiveCommand<Unit, Unit> ShowPasswordsCommand { get; }

    public ReactiveCommand<Unit, Unit> ShowProfileCommand { get; }

    public ReactiveCommand<Unit, Unit> ShowDevicesCommand { get; }

    public ReactiveCommand<Unit, Unit> ShowLoginCommand { get; }

    public ReactiveCommand<Unit, Unit> ShowRegistrationCommand { get; }

    public ReactiveCommand<Unit, Unit> LogoutCommand { get; }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    public ReactiveCommand<Unit, Unit> RefreshVisiblePageCommand { get; }

    public string AppTitle => GetTranslation("AppTitle");

    public string SettingsLabel => GetTranslation("Settings");

    public string SettingsLanguageLabel => GetTranslation("Settings_Language");

    public string SettingsThemeLabel => GetTranslation("Settings_Theme");

    public string EnglishLanguageDisplayName => GetTranslation("Language_English");

    public string HungarianLanguageDisplayName => GetTranslation("Language_Hungarian");

    public string ThemeLightLabel => GetTranslation("Theme_Light");

    public string ThemeDarkLabel => GetTranslation("Theme_Dark");

    public string PasswordVaultLabel => GetTranslation("Shell_PasswordVault");

    public string ProfileLabel => GetTranslation("Shell_Profile");

    public string DevicesLabel => GetTranslation("Shell_Devices");

    public string LogoutLabel => GetTranslation("Shell_Logout");

    public string WelcomeLabel => GetTranslation("Shell_Welcome");

    public string NavigationLabel => GetTranslation("Shell_Navigation");

    public string RefreshButtonLabel => GetTranslation("Common_Refresh");

    public string RefreshVisiblePageLabel => $"↻ {GetTranslation("Shell_RefreshVisiblePage")}";

    public string HeaderSubtitle => IsAuthenticated ? CurrentUserSubtitle : StatusMessage ?? string.Empty;

    public bool HasHeaderSubtitle => !string.IsNullOrWhiteSpace(HeaderSubtitle);

    public async Task InitializeAsync()
    {
        if (IsAuthenticated)
            return;

        StatusMessage = GetTranslation("Shell_BackendStarting");

        try
        {
            var rememberedTokens = await _endpoints.InicializeAllRememberMeAsync();

            foreach (var token in rememberedTokens)
            {
                _authSessionRegistry.TryAdd(token);
            }

            var currentToken = _authSessionRegistry.CurrentUserToken;
            if (currentToken != Guid.Empty)
            {
                await LoadAuthenticatedStateAsync(currentToken, GetTranslation("Shell_RememberedSessionLoaded"));
                return;
            }

            if (!IsAuthenticated)
                StatusMessage = null;
        }
        catch
        {
            if (!IsAuthenticated)
                StatusMessage = GetTranslation("Shell_BackendStartupFailed");
        }
    }

    protected override void OnLanguageChanged()
    {
        this.RaisePropertyChanged(nameof(AppTitle));
        this.RaisePropertyChanged(nameof(SettingsLabel));
        this.RaisePropertyChanged(nameof(SettingsLanguageLabel));
        this.RaisePropertyChanged(nameof(SettingsThemeLabel));
        this.RaisePropertyChanged(nameof(EnglishLanguageDisplayName));
        this.RaisePropertyChanged(nameof(HungarianLanguageDisplayName));
        this.RaisePropertyChanged(nameof(ThemeLightLabel));
        this.RaisePropertyChanged(nameof(ThemeDarkLabel));
        this.RaisePropertyChanged(nameof(PasswordVaultLabel));
        this.RaisePropertyChanged(nameof(ProfileLabel));
        this.RaisePropertyChanged(nameof(DevicesLabel));
        this.RaisePropertyChanged(nameof(LogoutLabel));
        this.RaisePropertyChanged(nameof(WelcomeLabel));
        this.RaisePropertyChanged(nameof(NavigationLabel));
        this.RaisePropertyChanged(nameof(RefreshButtonLabel));
        this.RaisePropertyChanged(nameof(RefreshVisiblePageLabel));
        this.RaisePropertyChanged(nameof(HeaderSubtitle));
        this.RaisePropertyChanged(nameof(HasHeaderSubtitle));
    }

    private void NavigateToLogin()
    {
        CurrentPageViewModel = LoginViewModel;
        StatusMessage = null;
    }

    private void NavigateToRegistration()
    {
        CurrentPageViewModel = RegistrationViewModel;
        StatusMessage = null;
    }

    private void NavigateToPasswords()
    {
        if (!IsAuthenticated)
        {
            return;
        }

        CurrentPageViewModel = PasswordsViewModel;
    }

    private void NavigateToProfile()
    {
        if (!IsAuthenticated)
        {
            return;
        }

        ProfileViewModel.ShowProfileMainPage();
        CurrentPageViewModel = ProfileViewModel;
    }

    private void NavigateToDevices()
    {
        if (!IsAuthenticated)
        {
            return;
        }

        ProfileViewModel.ShowDevicesMainPage();
        CurrentPageViewModel = ProfileViewModel;
    }

    private async Task OnAuthenticationSucceededAsync(Guid token)
    {
        _authSessionRegistry.TryAdd(token);
        await LoadAuthenticatedStateAsync(token, GetTranslation("Shell_SignedIn"));
    }

    private async Task LoadAuthenticatedStateAsync(Guid token, string? message = null)
    {
        var profile = await _endpoints.GetUserProfileInfoAsync(token);

        IsAuthenticated = true;
        CurrentUserDisplayName = BuildDisplayName(profile);
        CurrentUserSubtitle = string.IsNullOrWhiteSpace(profile.Username)
            ? profile.Email
            : $"@{profile.Username}";

        await PasswordsViewModel.LoadAsync(token);
        await ProfileViewModel.LoadAsync(token, profile);

        CurrentPageViewModel = PasswordsViewModel;
        StatusMessage = message;
        StartSessionMonitor(token);
    }

    private async Task RefreshVisiblePageAsync()
    {
        var token = _authSessionRegistry.CurrentUserToken;
        if (token == Guid.Empty || !IsAuthenticated)
        {
            return;
        }

        if (ReferenceEquals(CurrentPageViewModel, PasswordsViewModel))
        {
            await PasswordsViewModel.RefreshCurrentDataAsync();
            StatusMessage = GetTranslation("Shell_DataRefreshed");
            return;
        }

        if (ReferenceEquals(CurrentPageViewModel, ProfileViewModel))
        {
            if (ProfileViewModel.IsDevicesMainPage)
            {
                await ProfileViewModel.RefreshDevicesOnlyAsync();
            }
            else
            {
                await RefreshProfileDataAsync();
            }

            StatusMessage = GetTranslation("Shell_DataRefreshed");
            return;
        }

        await RefreshAuthenticatedStateAsync();
    }

    private async Task RefreshProfileDataAsync()
    {
        var token = _authSessionRegistry.CurrentUserToken;
        if (token == Guid.Empty)
        {
            return;
        }

        var profile = await _endpoints.GetUserProfileInfoAsync(token);

        CurrentUserDisplayName = BuildDisplayName(profile);
        CurrentUserSubtitle = string.IsNullOrWhiteSpace(profile.Username)
            ? profile.Email
            : $"@{profile.Username}";

        await ProfileViewModel.LoadAsync(token, profile);
    }

    private async Task RefreshAuthenticatedStateAsync()
    {
        var token = _authSessionRegistry.CurrentUserToken;
        if (token == Guid.Empty)
        {
            return;
        }

        await LoadAuthenticatedStateAsync(token, GetTranslation("Shell_DataRefreshed"));
    }

    private async Task LogoutAsync()
    {
        StopSessionMonitor();
        var token = _authSessionRegistry.CurrentUserToken;
        if (token == Guid.Empty)
        {
            NavigateToLogin();
            return;
        }

        try
        {
            _endpoints.Logout(token);
        }
        finally
        {
            _authSessionRegistry.TryRemove(token);
            await HandleLoggedOutStateAsync();
        }
    }

    private async Task HandleAccountDeletedAsync()
    {
        var token = _authSessionRegistry.CurrentUserToken;
        if (token != Guid.Empty)
        {
            _authSessionRegistry.TryRemove(token);
        }

        await HandleLoggedOutStateAsync();
        StatusMessage = GetTranslation("Profile_Delete_Success");
    }

    private Task HandleLoggedOutStateAsync(string? message = null)
    {
        StopSessionMonitor();
        IsAuthenticated = false;
        CurrentUserDisplayName = string.Empty;
        CurrentUserSubtitle = string.Empty;
        PasswordsViewModel.Reset();
        ProfileViewModel.Reset();
        LoginViewModel.Reset();
        RegistrationViewModel.Reset();
        CurrentPageViewModel = LoginViewModel;
        StatusMessage = message ?? GetTranslation("Shell_LoggedOut");
        return Task.CompletedTask;
    }

    private void StartSessionMonitor(Guid token)
    {
        StopSessionMonitor();

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };

        timer.Tick += async (_, _) => await CheckCurrentSessionAsync(token);
        _sessionMonitorTimer = timer;
        timer.Start();
    }


    private void StopSessionMonitor()
    {
        if (_sessionMonitorTimer is null)
            return;

        _sessionMonitorTimer.Stop();
        _sessionMonitorTimer = null;
        _isCheckingSession = false;
    }


    private async Task CheckCurrentSessionAsync(Guid token)
    {
        if (_isCheckingSession || !IsAuthenticated || token == Guid.Empty || token != _authSessionRegistry.CurrentUserToken)
            return;

        try
        {
            _isCheckingSession = true;
            var status = await _endpoints.GetAuthSessionStatusAsync(token);

            if (!status.IsAuthenticated)
                await HandleInvalidatedSessionAsync(token, status.InvalidationReason);
        }
        catch
        {
        }
        finally
        {
            _isCheckingSession = false;
        }
    }


    private async Task HandleInvalidatedSessionAsync(Guid token, AuthSessionInvalidationReason reason)
    {
        _authSessionRegistry.TryRemove(token);
        await HandleLoggedOutStateAsync(GetSessionInvalidationMessage(reason));
    }


    private string GetSessionInvalidationMessage(AuthSessionInvalidationReason reason) =>
        reason switch
        {
            AuthSessionInvalidationReason.ProfilePasswordChanged => GetTranslation("Shell_ProfilePasswordChangedLoggedOut"),
            AuthSessionInvalidationReason.ProfileRemoved => GetTranslation("Shell_ProfileRemovedLoggedOut"),
            AuthSessionInvalidationReason.Expired => GetTranslation("Shell_SessionExpired"),
            _ => GetTranslation("Shell_LoggedOut")
        };


    private static string BuildDisplayName(UserProfileInfoResponse profile)
    {
        var fullName = string.Join(
            " ",
            new[] { profile.LastName?.Trim(), profile.FirstName?.Trim() }
                .Where(part => !string.IsNullOrWhiteSpace(part)));

        if (!string.IsNullOrWhiteSpace(fullName))
        {
            return fullName;
        }

        if (!string.IsNullOrWhiteSpace(profile.Username))
        {
            return profile.Username;
        }

        return profile.Email;
    }
}
