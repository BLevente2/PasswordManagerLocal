using Avalonia.Media;
using Avalonia.Threading;
using PasswordManagerLocal.Abstractions.Services;
using PasswordManagerLocal.Exceptions;
using PasswordManagerLocal.Localization;
using PasswordManagerLocal.Services;
using PasswordManagerLocal.ViewModels.Auth;
using PasswordManagerLocal.ViewModels.Pages;
using PasswordManagerLocalBackend;
using PasswordManagerLocalBackend.Abstractions;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Responses;
using ReactiveUI;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;

namespace PasswordManagerLocal.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private static readonly TimeSpan SessionRenewalWarningLeadTime = TimeSpan.FromMinutes(1);
    private static readonly IBrush LightNavigationFrameBrush = Brush.Parse("#FF1F5FBF");
    private static readonly IBrush DarkNavigationFrameBrush = Brush.Parse("#FF7DB3FF");

    private readonly IEndpoints _endpoints;
    private readonly IAuthSessionRegistry _authSessionRegistry;

    private ViewModelBase _currentPageViewModel;
    private MainPageContentViewModel _currentAnimatedPageViewModel;
    private bool _isPageTransitionReversed;
    private bool _isAuthenticated;
    private string _currentUserDisplayName = string.Empty;
    private string _currentUserSubtitle = string.Empty;
    private DispatcherTimer? _sessionMonitorTimer;
    private bool _isCheckingSession;
    private bool _isSessionRenewalDialogOpen;
    private bool _isRenewingSession;
    private bool _isAddingProfile;
    private bool _isStartupProfileSelection;
    private bool _isCurrentRememberMeEnabled;
    private bool _isApplyingRememberMeFromSession;
    private bool _isSettingRememberMe;
    private int _profileChangeReturnPageIndex;
    private Guid _sessionRenewalDialogToken = Guid.Empty;
    private Guid _sessionRenewalPromptShownForToken = Guid.Empty;
    private string _sessionRenewalDialogProfileName = string.Empty;
    private readonly HashSet<Guid> _autoRenewingSessionTokens = new();
    private DatabaseRecoveryStage _databaseRecoveryStage;
    private DatabaseVersionNotSupportedException? _databaseVersionException;
    private bool _isResettingDatabase;

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
        ChangeProfileViewModel = new ChangeProfileViewModel(
            uiPreferences,
            _endpoints,
            _authSessionRegistry,
            NavigateBackFromChangeProfileAsync,
            NavigateToLoginAnotherProfile,
            SwitchToProfileAsync);

        _currentPageViewModel = LoginViewModel;
        _currentAnimatedPageViewModel = new MainPageContentViewModel(LoginViewModel);

        SetHungarianLanguageCommand = ReactiveCommand.Create(() => { UiPreferences.CurrentLanguage = AppLanguage.Hungarian; });
        SetEnglishLanguageCommand = ReactiveCommand.Create(() => { UiPreferences.CurrentLanguage = AppLanguage.English; });
        SetLightThemeCommand = ReactiveCommand.Create(() => { UiPreferences.CurrentThemeMode = AppThemeMode.Light; });
        SetDarkThemeCommand = ReactiveCommand.Create(() => { UiPreferences.CurrentThemeMode = AppThemeMode.Dark; });
        ShowPasswordsCommand = ReactiveCommand.Create(NavigateToPasswords);
        ShowProfileCommand = ReactiveCommand.Create(NavigateToProfile);
        ShowDevicesCommand = ReactiveCommand.Create(NavigateToDevices);
        ShowLoginCommand = ReactiveCommand.Create(NavigateToLogin);
        ShowRegistrationCommand = ReactiveCommand.Create(NavigateToRegistration);
        ShowChangeProfileCommand = ReactiveCommand.CreateFromTask(ShowChangeProfileAsync);
        LogoutCommand = ReactiveCommand.CreateFromTask(LogoutAsync);
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAuthenticatedStateAsync);
        RefreshVisiblePageCommand = ReactiveCommand.CreateFromTask(RefreshAllLoadedDataAsync);
        ConfirmSessionRenewalCommand = ReactiveCommand.CreateFromTask(ConfirmSessionRenewalAsync);
        DeclineSessionRenewalCommand = ReactiveCommand.Create(DeclineSessionRenewal);
        DatabaseRecoveryPrimaryCommand = ReactiveCommand.CreateFromTask(HandleDatabaseRecoveryPrimaryActionAsync);
        DatabaseRecoverySecondaryCommand = ReactiveCommand.Create(HandleDatabaseRecoverySecondaryAction);
        SensitiveDataVisibilityService.HideVisibleSecretsRequested += HandleHideVisibleSecretsRequested;
    }

    public LoginViewModel LoginViewModel { get; }

    public RegistrationViewModel RegistrationViewModel { get; }

    public PasswordsViewModel PasswordsViewModel { get; }

    public ProfileViewModel ProfileViewModel { get; }

    public ChangeProfileViewModel ChangeProfileViewModel { get; }

    public ViewModelBase CurrentPageViewModel
    {
        get => _currentPageViewModel;
        private set
        {
            if (ReferenceEquals(_currentPageViewModel, value))
            {
                return;
            }

            _currentPageViewModel.OnNavigatedFrom();
            ClearStatusMessage();
            this.RaiseAndSetIfChanged(ref _currentPageViewModel, value);
            CurrentAnimatedPageViewModel = new MainPageContentViewModel(value);
            RaiseNavigationStateProperties();
        }
    }

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isAuthenticated, value);
            this.RaisePropertyChanged(nameof(IsAnonymous));
            RaiseHeaderSubtitleProperties();
            this.RaisePropertyChanged(nameof(IsDesktopContentVisible));
            this.RaisePropertyChanged(nameof(IsDesktopNavigationVisible));
            this.RaisePropertyChanged(nameof(IsMobilePageIndicatorVisible));
            this.RaisePropertyChanged(nameof(IsPasswordsMainPageSelected));
            this.RaisePropertyChanged(nameof(IsDevicesMainPageSelected));
            this.RaisePropertyChanged(nameof(IsProfileMainPageSelected));
            this.RaisePropertyChanged(nameof(PasswordsNavigationFrameBrush));
            this.RaisePropertyChanged(nameof(DevicesNavigationFrameBrush));
            this.RaisePropertyChanged(nameof(ProfileNavigationFrameBrush));
            this.RaisePropertyChanged(nameof(CanChangeRememberMe));
        }
    }

    public bool IsAnonymous => !IsAuthenticated;

    public bool IsSessionRenewalDialogOpen
    {
        get => _isSessionRenewalDialogOpen;
        private set => this.RaiseAndSetIfChanged(ref _isSessionRenewalDialogOpen, value);
    }

    public bool IsRenewingSession
    {
        get => _isRenewingSession;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isRenewingSession, value);
            this.RaisePropertyChanged(nameof(SessionRenewalYesButtonLabel));
        }
    }

    public MainPageContentViewModel CurrentAnimatedPageViewModel
    {
        get => _currentAnimatedPageViewModel;
        private set => this.RaiseAndSetIfChanged(ref _currentAnimatedPageViewModel, value);
    }

    public bool IsPageTransitionReversed
    {
        get => _isPageTransitionReversed;
        private set => this.RaiseAndSetIfChanged(ref _isPageTransitionReversed, value);
    }

    public bool IsMobileNavigationEnabled => OperatingSystem.IsAndroid();

    public bool IsDesktopContentVisible => !IsMobileNavigationEnabled;

    public bool IsDesktopNavigationVisible => IsAuthenticated && !IsMobileNavigationEnabled && IsMainContentPageVisible;

    public bool IsMobilePageIndicatorVisible => IsAuthenticated && IsMobileNavigationEnabled && IsMainContentPageVisible;

    public bool IsPasswordsMainPageSelected => IsAuthenticated && CurrentMainPageIndex == 0;

    public bool IsDevicesMainPageSelected => IsAuthenticated && CurrentMainPageIndex == 1;

    public bool IsProfileMainPageSelected => IsAuthenticated && CurrentMainPageIndex == 2;

    public IBrush PasswordsNavigationFrameBrush => IsPasswordsMainPageSelected
        ? SelectedNavigationFrameBrush
        : Brushes.Transparent;

    public IBrush DevicesNavigationFrameBrush => IsDevicesMainPageSelected
        ? SelectedNavigationFrameBrush
        : Brushes.Transparent;

    public IBrush ProfileNavigationFrameBrush => IsProfileMainPageSelected
        ? SelectedNavigationFrameBrush
        : Brushes.Transparent;

    private IBrush SelectedNavigationFrameBrush => CurrentThemeMode == AppThemeMode.Light
        ? LightNavigationFrameBrush
        : DarkNavigationFrameBrush;

    private bool IsMainContentPageVisible => ReferenceEquals(CurrentPageViewModel, PasswordsViewModel) || ReferenceEquals(CurrentPageViewModel, ProfileViewModel);

    public int CurrentMobileNavigationIndex => CurrentMainPageIndex;

    public string MobileCurrentPageLabel => CurrentMainPageIndex switch
    {
        1 => DevicesLabel,
        2 => ProfileLabel,
        _ => PasswordVaultLabel
    };

    public string MobilePageIndicatorText => CurrentMainPageIndex switch
    {
        1 => "○ ● ○",
        2 => "○ ○ ●",
        _ => "● ○ ○"
    };


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
            RaiseHeaderSubtitleProperties();
        }
    }

    public bool IsCurrentRememberMeEnabled
    {
        get => _isCurrentRememberMeEnabled;
        set
        {
            if (_isCurrentRememberMeEnabled == value)
                return;

            var previousValue = _isCurrentRememberMeEnabled;
            this.RaiseAndSetIfChanged(ref _isCurrentRememberMeEnabled, value);

            if (!_isApplyingRememberMeFromSession)
                _ = SetCurrentRememberMeAsync(value, previousValue);
        }
    }

    public bool IsSettingRememberMe
    {
        get => _isSettingRememberMe;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isSettingRememberMe, value);
            this.RaisePropertyChanged(nameof(CanChangeRememberMe));
        }
    }

    public bool CanChangeRememberMe => IsAuthenticated && !IsSettingRememberMe;

    public ReactiveCommand<Unit, Unit> SetHungarianLanguageCommand { get; }

    public ReactiveCommand<Unit, Unit> SetEnglishLanguageCommand { get; }

    public ReactiveCommand<Unit, Unit> SetLightThemeCommand { get; }

    public ReactiveCommand<Unit, Unit> SetDarkThemeCommand { get; }

    public ReactiveCommand<Unit, Unit> ShowPasswordsCommand { get; }

    public ReactiveCommand<Unit, Unit> ShowProfileCommand { get; }

    public ReactiveCommand<Unit, Unit> ShowDevicesCommand { get; }

    public ReactiveCommand<Unit, Unit> ShowLoginCommand { get; }

    public ReactiveCommand<Unit, Unit> ShowRegistrationCommand { get; }

    public ReactiveCommand<Unit, Unit> ShowChangeProfileCommand { get; }

    public ReactiveCommand<Unit, Unit> LogoutCommand { get; }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    public ReactiveCommand<Unit, Unit> RefreshVisiblePageCommand { get; }

    public ReactiveCommand<Unit, Unit> ConfirmSessionRenewalCommand { get; }

    public ReactiveCommand<Unit, Unit> DeclineSessionRenewalCommand { get; }

    public ReactiveCommand<Unit, Unit> DatabaseRecoveryPrimaryCommand { get; }

    public ReactiveCommand<Unit, Unit> DatabaseRecoverySecondaryCommand { get; }

    public bool IsDatabaseRecoveryDialogOpen => _databaseRecoveryStage != DatabaseRecoveryStage.None;

    public bool IsApplicationInteractionEnabled => !IsDatabaseRecoveryDialogOpen;

    public bool IsDatabaseResetInProgress => _isResettingDatabase;

    public bool IsDatabaseRecoverySecondaryButtonVisible =>
        !_isResettingDatabase && _databaseRecoveryStage != DatabaseRecoveryStage.Declined;

    public bool IsDatabaseRecoveryPrimaryButtonVisible => !_isResettingDatabase;

    public string DatabaseRecoveryTitle => _databaseRecoveryStage switch
    {
        DatabaseRecoveryStage.FinalConfirmation => GetTranslation("DatabaseVersion_ResetConfirm_Title"),
        DatabaseRecoveryStage.Declined => GetTranslation("DatabaseVersion_Blocked_Title"),
        DatabaseRecoveryStage.ResetFailed => GetTranslation("DatabaseVersion_ResetFailed_Title"),
        _ => GetTranslation("DatabaseVersion_Error_Title")
    };

    public string DatabaseRecoveryMessage
    {
        get
        {
            if (_isResettingDatabase)
                return GetTranslation("DatabaseVersion_Resetting_Message");

            return _databaseRecoveryStage switch
            {
                DatabaseRecoveryStage.FinalConfirmation => GetTranslation("DatabaseVersion_ResetConfirm_Message"),
                DatabaseRecoveryStage.Declined => GetTranslation("DatabaseVersion_ResetDeclined_Message"),
                DatabaseRecoveryStage.ResetFailed => GetTranslation("DatabaseVersion_ResetFailed_Message"),
                _ => BuildDatabaseVersionErrorMessage()
            };
        }
    }

    public string DatabaseRecoveryPrimaryButtonLabel =>
        _databaseRecoveryStage == DatabaseRecoveryStage.FinalConfirmation
            ? GetTranslation("DatabaseVersion_ResetConfirm_Button")
            : GetTranslation("DatabaseVersion_Reset_Button");

    public string DatabaseRecoverySecondaryButtonLabel =>
        _databaseRecoveryStage == DatabaseRecoveryStage.FinalConfirmation ||
        _databaseRecoveryStage == DatabaseRecoveryStage.ResetFailed
            ? GetTranslation("Common_Back")
            : GetTranslation("DatabaseVersion_DoNotReset_Button");

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

    public string ChangeProfileLabel => GetTranslation("Shell_ChangeProfile");

    public string RememberMeDropdownLabel => GetTranslation("Shell_RememberMeSwitch");

    public string RememberMeOnLabel => GetTranslation("Common_On");

    public string RememberMeOffLabel => GetTranslation("Common_Off");

    public string WelcomeLabel => GetTranslation("Shell_Welcome");

    public string NavigationLabel => GetTranslation("Shell_Navigation");

    public string RefreshButtonLabel => GetTranslation("Common_Refresh");

    public string RefreshVisiblePageLabel => $"↻ {GetTranslation("Shell_RefreshVisiblePage")}";

    public string YesLabel => GetTranslation("Common_Yes");

    public string NoLabel => GetTranslation("Common_No");

    public string LogoutConfirmationTitle => GetTranslation("Shell_LogoutConfirm_Title");

    public string LogoutConfirmationMessage => GetTranslation("Shell_LogoutConfirm_Message");

    public string ExitConfirmationTitle => GetTranslation("Shell_ExitConfirm_Title");

    public string ExitConfirmationMessage => GetTranslation("Shell_ExitConfirm_Message");

    public string SessionRenewalWarningTitle => GetTranslation("Shell_SessionRenewal_Title");

    public string SessionRenewalWarningMessage => string.IsNullOrWhiteSpace(_sessionRenewalDialogProfileName)
        ? GetTranslation("Shell_SessionRenewal_Message")
        : string.Format(GetTranslation("Shell_SessionRenewal_ProfileMessage"), _sessionRenewalDialogProfileName);

    public string SessionRenewalYesButtonLabel => IsRenewingSession
        ? GetTranslation("Common_Loading")
        : YesLabel;

    public string HeaderSubtitle => !string.IsNullOrWhiteSpace(StatusMessage)
        ? StatusMessage
        : IsAuthenticated ? CurrentUserSubtitle : string.Empty;

    public bool HasHeaderSubtitle => !string.IsNullOrWhiteSpace(HeaderSubtitle);

    public bool IsHeaderSubtitleError => IsStatusMessageError;

    public bool HasNonErrorHeaderSubtitle => HasHeaderSubtitle && !IsHeaderSubtitleError;

    public async Task InitializeAsync()
    {
        if (IsAuthenticated)
            return;

        ShowInformationMessage(GetTranslation("Shell_BackendStarting"));

        try
        {
            var rememberedTokens = await _endpoints.InicializeAllRememberMeAsync();
            var loadedRememberedTokens = new List<Guid>();

            foreach (var token in rememberedTokens)
            {
                if (await TryAddRememberedSessionAsync(token, false))
                    loadedRememberedTokens.Add(token);
            }

            if (loadedRememberedTokens.Count == 1)
            {
                await LoadAuthenticatedStateAsync(loadedRememberedTokens[0], GetTranslation("Shell_RememberedSessionLoaded"));
                return;
            }

            if (loadedRememberedTokens.Count > 1)
            {
                await ShowStartupProfileSelectionAsync(GetTranslation("Shell_ChooseRememberedProfile"));
                EnsureSessionMonitor();
                return;
            }

            if (!IsAuthenticated)
                ClearStatusMessage();
        }
        catch (DatabaseVersionNotSupportedException exception)
        {
            if (!IsAuthenticated)
                ShowDatabaseRecoveryDialog(exception);
        }
        catch
        {
            if (!IsAuthenticated)
                ShowErrorMessage(GetTranslation("Shell_BackendStartupFailed"));
        }
    }

    public async Task<bool> TryNavigateBackAsync()
    {
        if (IsDatabaseRecoveryDialogOpen)
        {
            if (!_isResettingDatabase)
                HandleDatabaseRecoverySecondaryAction();

            return true;
        }

        if (ReferenceEquals(CurrentPageViewModel, PasswordsViewModel))
        {
            return PasswordsViewModel.TryNavigateBack();
        }

        if (ReferenceEquals(CurrentPageViewModel, ProfileViewModel))
        {
            return ProfileViewModel.TryNavigateBack();
        }

        if (ReferenceEquals(CurrentPageViewModel, ChangeProfileViewModel))
        {
            if (_isStartupProfileSelection)
                return false;

            await NavigateBackFromChangeProfileAsync();
            return true;
        }

        if (ReferenceEquals(CurrentPageViewModel, LoginViewModel))
        {
            if (await LoginViewModel.TryNavigateBackAsync())
                return true;

            if (_isAddingProfile)
            {
                await NavigateBackFromAddProfileAsync();
                return true;
            }

            return false;
        }

        if (ReferenceEquals(CurrentPageViewModel, RegistrationViewModel))
        {
            if (_isAddingProfile)
                await NavigateBackFromAddProfileAsync();
            else
                NavigateToLogin();

            return true;
        }

        return false;
    }

    public Task RequestLogoutAsync() => LogoutAsync();

    private void HandleHideVisibleSecretsRequested(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            HideVisibleSensitiveData();
            return;
        }

        Dispatcher.UIThread.Post(HideVisibleSensitiveData);
    }


    private void HideVisibleSensitiveData()
    {
        PasswordsViewModel.HideVisibleSensitiveData();
    }


    private void ShowDatabaseRecoveryDialog(DatabaseVersionNotSupportedException exception)
    {
        _databaseVersionException = exception;
        _databaseRecoveryStage = DatabaseRecoveryStage.CompatibilityError;
        _isResettingDatabase = false;
        ClearStatusMessage();
        RaiseDatabaseRecoveryProperties();
    }

    private async Task HandleDatabaseRecoveryPrimaryActionAsync()
    {
        if (_isResettingDatabase || !IsDatabaseRecoveryDialogOpen)
            return;

        if (_databaseRecoveryStage != DatabaseRecoveryStage.FinalConfirmation)
        {
            _databaseRecoveryStage = DatabaseRecoveryStage.FinalConfirmation;
            RaiseDatabaseRecoveryProperties();
            return;
        }

        _isResettingDatabase = true;
        RaiseDatabaseRecoveryProperties();

        try
        {
            await BackendHost.ResetDatabaseAndReinitializeAsync();
            _databaseVersionException = null;
            _databaseRecoveryStage = DatabaseRecoveryStage.None;
            ClearStatusMessage();
            RaiseDatabaseRecoveryProperties();
        }
        catch
        {
            _databaseRecoveryStage = DatabaseRecoveryStage.ResetFailed;
        }
        finally
        {
            _isResettingDatabase = false;
            RaiseDatabaseRecoveryProperties();
        }
    }

    private void HandleDatabaseRecoverySecondaryAction()
    {
        if (_isResettingDatabase || !IsDatabaseRecoveryDialogOpen)
            return;

        _databaseRecoveryStage = _databaseRecoveryStage switch
        {
            DatabaseRecoveryStage.FinalConfirmation => DatabaseRecoveryStage.CompatibilityError,
            DatabaseRecoveryStage.ResetFailed => DatabaseRecoveryStage.CompatibilityError,
            DatabaseRecoveryStage.CompatibilityError => DatabaseRecoveryStage.Declined,
            _ => _databaseRecoveryStage
        };

        RaiseDatabaseRecoveryProperties();
    }

    private string BuildDatabaseVersionErrorMessage()
    {
        if (_databaseVersionException?.DetectedVersion is int detectedVersion)
        {
            return string.Format(
                GetTranslation("DatabaseVersion_Error_Message"),
                detectedVersion,
                _databaseVersionException.OldestSupportedVersion,
                _databaseVersionException.CurrentVersion);
        }

        return GetTranslation("DatabaseVersion_InvalidHeader_Message");
    }

    private void RaiseDatabaseRecoveryProperties()
    {
        this.RaisePropertyChanged(nameof(IsDatabaseRecoveryDialogOpen));
        this.RaisePropertyChanged(nameof(IsApplicationInteractionEnabled));
        this.RaisePropertyChanged(nameof(IsDatabaseResetInProgress));
        this.RaisePropertyChanged(nameof(IsDatabaseRecoverySecondaryButtonVisible));
        this.RaisePropertyChanged(nameof(IsDatabaseRecoveryPrimaryButtonVisible));
        this.RaisePropertyChanged(nameof(DatabaseRecoveryTitle));
        this.RaisePropertyChanged(nameof(DatabaseRecoveryMessage));
        this.RaisePropertyChanged(nameof(DatabaseRecoveryPrimaryButtonLabel));
        this.RaisePropertyChanged(nameof(DatabaseRecoverySecondaryButtonLabel));
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
        this.RaisePropertyChanged(nameof(ChangeProfileLabel));
        this.RaisePropertyChanged(nameof(RememberMeDropdownLabel));
        this.RaisePropertyChanged(nameof(RememberMeOnLabel));
        this.RaisePropertyChanged(nameof(RememberMeOffLabel));
        this.RaisePropertyChanged(nameof(WelcomeLabel));
        this.RaisePropertyChanged(nameof(NavigationLabel));
        this.RaisePropertyChanged(nameof(RefreshButtonLabel));
        this.RaisePropertyChanged(nameof(RefreshVisiblePageLabel));
        this.RaisePropertyChanged(nameof(YesLabel));
        this.RaisePropertyChanged(nameof(NoLabel));
        this.RaisePropertyChanged(nameof(LogoutConfirmationTitle));
        this.RaisePropertyChanged(nameof(LogoutConfirmationMessage));
        this.RaisePropertyChanged(nameof(ExitConfirmationTitle));
        this.RaisePropertyChanged(nameof(ExitConfirmationMessage));
        this.RaisePropertyChanged(nameof(SessionRenewalWarningTitle));
        this.RaisePropertyChanged(nameof(SessionRenewalWarningMessage));
        this.RaisePropertyChanged(nameof(SessionRenewalYesButtonLabel));
        RaiseDatabaseRecoveryProperties();
        RaiseHeaderSubtitleProperties();
        this.RaisePropertyChanged(nameof(MobileCurrentPageLabel));
    }

    protected override void OnThemeChanged()
    {
        this.RaisePropertyChanged(nameof(PasswordsNavigationFrameBrush));
        this.RaisePropertyChanged(nameof(DevicesNavigationFrameBrush));
        this.RaisePropertyChanged(nameof(ProfileNavigationFrameBrush));
    }

    protected override void OnStatusMessageChanged()
    {
        RaiseHeaderSubtitleProperties();
    }

    private void RaiseHeaderSubtitleProperties()
    {
        this.RaisePropertyChanged(nameof(HeaderSubtitle));
        this.RaisePropertyChanged(nameof(HasHeaderSubtitle));
        this.RaisePropertyChanged(nameof(IsHeaderSubtitleError));
        this.RaisePropertyChanged(nameof(HasNonErrorHeaderSubtitle));
    }

    private void ShowShellMessage(string? message, OperationMessageKind kind = OperationMessageKind.Success)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            ClearStatusMessage();
            return;
        }

        switch (kind)
        {
            case OperationMessageKind.Error:
                ShowErrorMessage(message);
                break;
            case OperationMessageKind.Information:
                ShowInformationMessage(message);
                break;
            default:
                ShowSuccessMessage(message);
                break;
        }
    }

    private void NavigateToLogin()
    {
        ConfigureAuthBackNavigation();
        CurrentPageViewModel = LoginViewModel;
        ClearStatusMessage();
    }

    private void NavigateToRegistration()
    {
        ConfigureAuthBackNavigation();
        CurrentPageViewModel = RegistrationViewModel;
        ClearStatusMessage();
    }

    private async Task ShowStartupProfileSelectionAsync(string? message = null)
    {
        _isAddingProfile = false;
        _isStartupProfileSelection = true;
        _profileChangeReturnPageIndex = 0;
        _authSessionRegistry.CurrentUserToken = Guid.Empty;
        ApplyRememberMeFromSession(false);
        IsAuthenticated = false;
        CurrentUserDisplayName = string.Empty;
        CurrentUserSubtitle = string.Empty;
        PasswordsViewModel.Reset();
        ProfileViewModel.Reset();
        ChangeProfileViewModel.SetStartupSelectionMode(true);
        ConfigureAuthBackNavigation();
        await ChangeProfileViewModel.LoadAsync();
        CurrentPageViewModel = ChangeProfileViewModel;
        ShowShellMessage(message, OperationMessageKind.Information);
    }

    private async Task ShowChangeProfileAsync()
    {
        if (!IsAuthenticated)
            return;

        _isAddingProfile = false;
        _isStartupProfileSelection = false;
        if (IsMainContentPageVisible)
            _profileChangeReturnPageIndex = CurrentMainPageIndex;

        ChangeProfileViewModel.SetStartupSelectionMode(false);
        ConfigureAuthBackNavigation();
        await ChangeProfileViewModel.LoadAsync();
        CurrentPageViewModel = ChangeProfileViewModel;
        ClearStatusMessage();
    }

    private void NavigateToLoginAnotherProfile()
    {
        if (!IsAuthenticated && !_isStartupProfileSelection)
            return;

        _isAddingProfile = true;
        LoginViewModel.Reset();
        RegistrationViewModel.Reset();
        ConfigureAuthBackNavigation();
        CurrentPageViewModel = LoginViewModel;
        ClearStatusMessage();
    }

    private void ConfigureAuthBackNavigation()
    {
        var isVisible = _isAddingProfile;
        LoginViewModel.SetBackNavigation(isVisible, isVisible ? NavigateBackFromAddProfileAsync : null);
        RegistrationViewModel.SetBackNavigation(isVisible, isVisible ? NavigateBackFromAddProfileAsync : null);
    }

    private async Task NavigateBackFromAddProfileAsync()
    {
        var wasStartupProfileSelection = _isStartupProfileSelection;
        _isAddingProfile = false;
        ConfigureAuthBackNavigation();
        LoginViewModel.Reset();
        RegistrationViewModel.Reset();

        if (wasStartupProfileSelection)
        {
            await ShowStartupProfileSelectionAsync();
            return;
        }

        await ShowChangeProfileAsync();
    }

    private async Task NavigateBackFromChangeProfileAsync()
    {
        var token = _authSessionRegistry.CurrentUserToken;
        if (token == Guid.Empty)
        {
            await HandleLoggedOutStateAsync();
            return;
        }

        await LoadAuthenticatedStateAsync(token);
        RestoreProfileChangeReturnPage();
    }

    private async Task SwitchToProfileAsync(Guid token)
    {
        var wasStartupSelection = _isStartupProfileSelection;
        _isStartupProfileSelection = false;
        if (!await LoadAuthenticatedStateAsync(token))
            return;

        if (!wasStartupSelection)
            RestoreProfileChangeReturnPage();

        ShowSuccessMessage(wasStartupSelection
            ? GetTranslation("Shell_RememberedSessionLoaded")
            : GetTranslation("Shell_ProfileChanged"));
    }

    private void RestoreProfileChangeReturnPage()
    {
        switch (_profileChangeReturnPageIndex)
        {
            case 1:
                NavigateToDevices();
                break;
            case 2:
                NavigateToProfile();
                break;
            default:
                NavigateToPasswords();
                break;
        }
    }

    private void NavigateToPasswords()
    {
        if (!IsAuthenticated)
        {
            return;
        }

        ClearStatusMessage();
        PasswordsViewModel.ShowMainPage();
        ProfileViewModel.DiscardTransientNavigationState();
        ShowMainContentPage(PasswordsViewModel);
    }

    private void NavigateToProfile()
    {
        if (!IsAuthenticated)
        {
            return;
        }

        ClearStatusMessage();
        PasswordsViewModel.ShowMainPage();
        ProfileViewModel.ShowProfileMainPage();
        ShowMainContentPage(ProfileViewModel);
    }

    private void NavigateToDevices()
    {
        if (!IsAuthenticated)
        {
            return;
        }

        ClearStatusMessage();
        PasswordsViewModel.ShowMainPage();
        ProfileViewModel.ShowDevicesMainPage();
        ShowMainContentPage(ProfileViewModel);
    }

    private void ShowMainContentPage(ViewModelBase pageViewModel)
    {
        if (!ReferenceEquals(CurrentPageViewModel, pageViewModel))
        {
            CurrentPageViewModel = pageViewModel;
            return;
        }

        CurrentAnimatedPageViewModel = new MainPageContentViewModel(pageViewModel);
        RaiseNavigationStateProperties();
    }

    public bool NavigateToNextMainPage()
    {
        if (!IsAuthenticated || !IsMainContentPageVisible)
        {
            return false;
        }

        IsPageTransitionReversed = false;

        return CurrentMainPageIndex switch
        {
            0 => NavigateToDevicesFromSwipe(),
            1 => NavigateToProfileFromSwipe(),
            _ => NavigateToPasswordsFromSwipe()
        };
    }

    public bool NavigateToPreviousMainPage()
    {
        if (!IsAuthenticated || !IsMainContentPageVisible)
        {
            return false;
        }

        IsPageTransitionReversed = true;

        return CurrentMainPageIndex switch
        {
            0 => NavigateToProfileFromSwipe(),
            1 => NavigateToPasswordsFromSwipe(),
            _ => NavigateToDevicesFromSwipe()
        };
    }

    private bool NavigateToPasswordsFromSwipe()
    {
        NavigateToPasswords();
        return true;
    }

    private bool NavigateToDevicesFromSwipe()
    {
        NavigateToDevices();
        return true;
    }

    private bool NavigateToProfileFromSwipe()
    {
        NavigateToProfile();
        return true;
    }

    private async Task<bool> TryAddRememberedSessionAsync(Guid token, bool select)
    {
        if (token == Guid.Empty)
            return false;

        try
        {
            var status = await _endpoints.GetAuthSessionStatusAsync(token);
            if (!status.IsAuthenticated)
                return false;

            var profile = await _endpoints.GetUserProfileInfoAsync(token);
            if (await ContainsActiveUserIdAsync(profile.UId, token))
            {
                try { _endpoints.Logout(token); } catch { }
                return false;
            }

            _authSessionRegistry.TryAdd(token, select);
            SetSessionProfile(token, profile);
            return true;
        }
        catch
        {
            try { _endpoints.Logout(token); } catch { }
            _authSessionRegistry.TryRemove(token);
            return false;
        }
    }


    private void ApplyRememberMeFromSession(bool isEnabled)
    {
        _isApplyingRememberMeFromSession = true;
        try
        {
            IsCurrentRememberMeEnabled = isEnabled;
        }
        finally
        {
            _isApplyingRememberMeFromSession = false;
        }
    }


    private async Task SetCurrentRememberMeAsync(bool isEnabled, bool previousValue)
    {
        var token = _authSessionRegistry.CurrentUserToken;
        if (token == Guid.Empty || !IsAuthenticated)
        {
            ApplyRememberMeFromSession(previousValue);
            return;
        }

        ClearStatusMessage();

        try
        {
            IsSettingRememberMe = true;
            await _endpoints.SetRememberMeAsync(token, isEnabled);
            _authSessionRegistry.TrySetRememberMe(token, isEnabled);
            ShowSuccessMessage(isEnabled
                ? GetTranslation("Shell_RememberMeEnabled")
                : GetTranslation("Shell_RememberMeDisabled"));
        }
        catch
        {
            ApplyRememberMeFromSession(previousValue);
            ShowErrorMessage(GetTranslation("Shell_RememberMeUpdateFailed"));
        }
        finally
        {
            IsSettingRememberMe = false;
        }
    }


    private async Task OnAuthenticationSucceededAsync(Guid token)
    {
        var profile = await _endpoints.GetUserProfileInfoAsync(token);
        var wasAddingProfile = _isAddingProfile;
        var wasStartupProfileSelection = _isStartupProfileSelection;

        if (await ContainsActiveUserIdAsync(profile.UId, token))
        {
            try { _endpoints.Logout(token); } catch { }
            throw new DuplicateActiveProfileException();
        }

        _authSessionRegistry.TryAdd(token);
        SetSessionProfile(token, profile);
        _isAddingProfile = false;
        _isStartupProfileSelection = false;
        ConfigureAuthBackNavigation();
        if (!await LoadAuthenticatedStateAsync(token, profile))
            return;

        if (wasAddingProfile && !wasStartupProfileSelection)
            RestoreProfileChangeReturnPage();

        ShowSuccessMessage(GetTranslation("Shell_SignedIn"));
    }

    private async Task<bool> LoadAuthenticatedStateAsync(
        Guid token,
        string? message = null,
        OperationMessageKind messageKind = OperationMessageKind.Success)
    {
        var profile = await _endpoints.GetUserProfileInfoAsync(token);
        return await LoadAuthenticatedStateAsync(token, profile, message, messageKind);
    }

    private async Task<bool> LoadAuthenticatedStateAsync(
        Guid token,
        UserProfileInfoResponse profile,
        string? message = null,
        OperationMessageKind messageKind = OperationMessageKind.Success)
    {
        _authSessionRegistry.CurrentUserToken = token;
        SetSessionProfile(token, profile);
        IsAuthenticated = true;
        CurrentUserDisplayName = BuildDisplayName(profile);
        CurrentUserSubtitle = BuildSubtitle(profile);
        ApplyRememberMeFromSession(profile.IsRememberMeEnabled);

        PasswordsViewModel.Reset();
        ProfileViewModel.Reset();
        var passwordsLoaded = await PasswordsViewModel.LoadAsync(token);
        var profileLoaded = await ProfileViewModel.LoadAsync(token, profile);

        CurrentPageViewModel = PasswordsViewModel;

        if (!passwordsLoaded)
        {
            var errorMessage = PasswordsViewModel.StatusMessage ?? GetTranslation("Error_Generic");
            PasswordsViewModel.ClearStatusMessage();
            ShowErrorMessage(errorMessage);
        }
        else if (!profileLoaded)
        {
            var errorMessage = ProfileViewModel.StatusMessage ?? GetTranslation("Error_Generic");
            ProfileViewModel.ClearStatusMessage();
            ShowErrorMessage(errorMessage);
        }
        else
        {
            ShowShellMessage(message, messageKind);
        }

        EnsureSessionMonitor();
        return passwordsLoaded && profileLoaded;
    }

    private async Task RefreshVisiblePageAsync()
    {
        var token = _authSessionRegistry.CurrentUserToken;
        if (token == Guid.Empty || !IsAuthenticated)
            return;

        ClearStatusMessage();

        if (ReferenceEquals(CurrentPageViewModel, PasswordsViewModel))
        {
            await PasswordsViewModel.RefreshCurrentDataAsync();
            return;
        }

        if (ReferenceEquals(CurrentPageViewModel, ProfileViewModel))
        {
            if (ProfileViewModel.IsDevicesMainPage)
                await ProfileViewModel.RefreshDevicesOnlyAsync();
            else
                await ProfileViewModel.RefreshCurrentDataAsync();

            return;
        }

        await RefreshAuthenticatedStateAsync();
    }

    private async Task RefreshAllLoadedDataAsync()
    {
        var token = _authSessionRegistry.CurrentUserToken;
        if (token == Guid.Empty || !IsAuthenticated)
            return;

        ClearStatusMessage();

        try
        {
            var profile = await _endpoints.GetUserProfileInfoAsync(token);

            CurrentUserDisplayName = BuildDisplayName(profile);
            CurrentUserSubtitle = BuildSubtitle(profile);
            ApplyRememberMeFromSession(profile.IsRememberMeEnabled);
            SetSessionProfile(token, profile);

            PasswordsViewModel.SetSessionToken(token);
            ProfileViewModel.SetSessionToken(token);

            var passwordsLoaded = await PasswordsViewModel.RefreshCurrentDataAsync(false);
            var profileLoaded = await ProfileViewModel.LoadAsync(token, profile);

            if (!passwordsLoaded)
            {
                var errorMessage = PasswordsViewModel.StatusMessage ?? GetTranslation("Error_Generic");
                PasswordsViewModel.ClearStatusMessage();
                ShowErrorMessage(errorMessage);
                return;
            }

            if (!profileLoaded)
            {
                var errorMessage = ProfileViewModel.StatusMessage ?? GetTranslation("Error_Generic");
                ProfileViewModel.ClearStatusMessage();
                ShowErrorMessage(errorMessage);
                return;
            }

            ShowSuccessMessage(GetTranslation("Shell_DataRefreshed"));
        }
        catch (Exception ex)
        {
            ShowErrorMessage(GetSafeErrorMessage(ex));
        }
    }

    private async Task<bool> RefreshProfileDataAsync()
    {
        var token = _authSessionRegistry.CurrentUserToken;
        if (token == Guid.Empty)
            return false;

        var profile = await _endpoints.GetUserProfileInfoAsync(token);

        CurrentUserDisplayName = BuildDisplayName(profile);
        CurrentUserSubtitle = BuildSubtitle(profile);
        ApplyRememberMeFromSession(profile.IsRememberMeEnabled);
        SetSessionProfile(token, profile);

        return await ProfileViewModel.LoadAsync(token, profile);
    }

    private async Task RefreshAuthenticatedStateAsync()
    {
        await RefreshAllLoadedDataAsync();
    }

    private async Task LogoutAsync()
    {
        var token = _authSessionRegistry.CurrentUserToken;
        if (token == Guid.Empty)
        {
            await HandleLoggedOutStateAsync();
            return;
        }

        try
        {
            _endpoints.Logout(token);
        }
        catch
        {
        }
        finally
        {
            _authSessionRegistry.TryRemove(token);
            await LoadNextAvailableSessionOrLoginAsync(GetTranslation("Shell_LoggedOut"));
        }
    }

    private async Task HandleAccountDeletedAsync()
    {
        var token = _authSessionRegistry.CurrentUserToken;
        if (token != Guid.Empty)
            _authSessionRegistry.TryRemove(token);

        await LoadNextAvailableSessionOrLoginAsync(GetTranslation("Profile_Delete_Success"));
    }

    private Task HandleLoggedOutStateAsync(string? message = null, OperationMessageKind messageKind = OperationMessageKind.Success)
    {
        StopSessionMonitor();
        IsSessionRenewalDialogOpen = false;
        IsRenewingSession = false;
        _sessionRenewalDialogToken = Guid.Empty;
        _sessionRenewalPromptShownForToken = Guid.Empty;
        _sessionRenewalDialogProfileName = string.Empty;
        _isAddingProfile = false;
        _isStartupProfileSelection = false;
        ConfigureAuthBackNavigation();
        IsAuthenticated = false;
        CurrentUserDisplayName = string.Empty;
        CurrentUserSubtitle = string.Empty;
        ApplyRememberMeFromSession(false);
        PasswordsViewModel.Reset();
        ProfileViewModel.Reset();
        LoginViewModel.Reset();
        RegistrationViewModel.Reset();
        CurrentPageViewModel = LoginViewModel;
        ShowShellMessage(message ?? GetTranslation("Shell_LoggedOut"), messageKind);
        return Task.CompletedTask;
    }

    private void EnsureSessionMonitor()
    {
        if (_sessionMonitorTimer is not null)
            return;

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };

        timer.Tick += async (_, _) => await CheckAllSessionsAsync();
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


    private async Task CheckAllSessionsAsync()
    {
        if (_isCheckingSession)
            return;

        var tokens = _authSessionRegistry.ListTokens();
        if (tokens.Count == 0)
        {
            if (IsAuthenticated)
                await HandleLoggedOutStateAsync();

            return;
        }

        try
        {
            _isCheckingSession = true;

            foreach (var token in tokens.ToList())
            {
                if (_authSessionRegistry.GetSession(token) is null)
                    continue;

                AuthSessionStatusResponse status;
                try
                {
                    status = await _endpoints.GetAuthSessionStatusAsync(token);
                }
                catch
                {
                    continue;
                }

                if (!status.IsAuthenticated)
                {
                    await HandleInvalidatedSessionAsync(token, status.InvalidationReason);
                    continue;
                }

                if (status.ExpiresAtUtc is { } expiresAtUtc)
                    await HandleSessionExpirationWarningAsync(token, expiresAtUtc);
            }
        }
        finally
        {
            _isCheckingSession = false;
        }
    }


    private async Task HandleSessionExpirationWarningAsync(Guid token, DateTimeOffset expiresAtUtc)
    {
        if (expiresAtUtc - DateTimeOffset.UtcNow > SessionRenewalWarningLeadTime)
            return;

        var session = _authSessionRegistry.GetSession(token);
        if (session?.IsRememberMeEnabled == true)
        {
            await AutoRenewRememberedSessionAsync(token);
            return;
        }

        ShowSessionRenewalWarning(token);
    }


    private void ShowSessionRenewalWarning(Guid token)
    {
        if (IsSessionRenewalDialogOpen || _sessionRenewalPromptShownForToken == token)
            return;

        _sessionRenewalDialogToken = token;
        _sessionRenewalPromptShownForToken = token;
        _sessionRenewalDialogProfileName = GetSessionDisplayName(token);
        this.RaisePropertyChanged(nameof(SessionRenewalWarningMessage));
        IsSessionRenewalDialogOpen = true;
    }


    private async Task AutoRenewRememberedSessionAsync(Guid token)
    {
        if (!_autoRenewingSessionTokens.Add(token))
            return;

        try
        {
            await RenewSessionTokenAsync(token);
        }
        catch
        {
        }
        finally
        {
            _autoRenewingSessionTokens.Remove(token);
        }
    }


    private async Task ConfirmSessionRenewalAsync()
    {
        if (IsRenewingSession)
            return;

        var oldToken = _sessionRenewalDialogToken;
        if (oldToken == Guid.Empty || _authSessionRegistry.GetSession(oldToken) is null || !IsAuthenticated)
        {
            CloseSessionRenewalDialog();
            return;
        }

        ClearStatusMessage();

        try
        {
            IsRenewingSession = true;
            await RenewSessionTokenAsync(oldToken);
            CloseSessionRenewalDialog(true);
            ShowSuccessMessage(GetTranslation("Shell_SessionRenewed"));
            EnsureSessionMonitor();
        }
        catch
        {
            CloseSessionRenewalDialog();
            ShowErrorMessage(GetTranslation("Shell_SessionRenewalFailed"));
        }
        finally
        {
            IsRenewingSession = false;
        }
    }


    private async Task RenewSessionTokenAsync(Guid oldToken)
    {
        var wasCurrent = oldToken == _authSessionRegistry.CurrentUserToken;
        var newToken = await _endpoints.RenewAuthSessionAsync(oldToken);

        if (!_authSessionRegistry.TryReplaceToken(oldToken, newToken))
        {
            _authSessionRegistry.TryAdd(newToken, wasCurrent);
            _authSessionRegistry.TryRemove(oldToken);
        }

        if (wasCurrent)
        {
            _authSessionRegistry.CurrentUserToken = newToken;
            PasswordsViewModel.SetSessionToken(newToken);
            ProfileViewModel.SetSessionToken(newToken);
        }

        if (_sessionRenewalPromptShownForToken == oldToken)
            _sessionRenewalPromptShownForToken = Guid.Empty;

        if (ReferenceEquals(CurrentPageViewModel, ChangeProfileViewModel))
            await ChangeProfileViewModel.LoadAsync();
    }


    private void DeclineSessionRenewal()
    {
        if (IsRenewingSession)
            return;

        CloseSessionRenewalDialog();
        ShowInformationMessage(GetTranslation("Shell_SessionRenewalDeclined"), true);
    }


    private async Task HandleInvalidatedSessionAsync(Guid token, AuthSessionInvalidationReason reason)
    {
        var wasCurrent = token == _authSessionRegistry.CurrentUserToken;
        var message = GetSessionInvalidationMessage(reason);
        var session = _authSessionRegistry.GetSession(token);

        if (reason == AuthSessionInvalidationReason.Expired && session?.IsRememberMeEnabled == true)
        {
            if (await TryRestoreRememberedSessionAsync(token, session, wasCurrent))
                return;
        }

        _authSessionRegistry.TryRemove(token);

        if (_sessionRenewalDialogToken == token)
            CloseSessionRenewalDialog();

        if (wasCurrent)
        {
            await LoadNextAvailableSessionOrLoginAsync(message, OperationMessageKind.Error);
            return;
        }

        if (_authSessionRegistry.ListTokens().Count == 0)
        {
            await HandleLoggedOutStateAsync(message, OperationMessageKind.Error);
            return;
        }

        if (ReferenceEquals(CurrentPageViewModel, ChangeProfileViewModel))
            await ChangeProfileViewModel.LoadAsync();
    }


    private async Task<bool> TryRestoreRememberedSessionAsync(Guid oldToken, AuthSessionProfile session, bool wasCurrent)
    {
        if (session.UserId == Guid.Empty)
            return false;

        try
        {
            var newToken = await _endpoints.InitializeRememberMeSessionAsync(session.UserId);

            if (!_authSessionRegistry.TryReplaceToken(oldToken, newToken))
            {
                _authSessionRegistry.TryAdd(newToken, wasCurrent);
                _authSessionRegistry.TryRemove(oldToken);
            }

            var profile = await _endpoints.GetUserProfileInfoAsync(newToken);
            SetSessionProfile(newToken, profile);

            if (_sessionRenewalDialogToken == oldToken)
                CloseSessionRenewalDialog(true);

            if (wasCurrent)
            {
                await LoadAuthenticatedStateAsync(newToken, profile, GetTranslation("Shell_RememberedSessionLoaded"));
                return true;
            }

            if (ReferenceEquals(CurrentPageViewModel, ChangeProfileViewModel))
                await ChangeProfileViewModel.LoadAsync();

            return true;
        }
        catch
        {
            return false;
        }
    }



    private async Task LoadNextAvailableSessionOrLoginAsync(string? message = null, OperationMessageKind messageKind = OperationMessageKind.Success)
    {
        var nextToken = _authSessionRegistry.CurrentUserToken;
        if (nextToken == Guid.Empty)
        {
            await HandleLoggedOutStateAsync(message, messageKind);
            return;
        }

        await LoadAuthenticatedStateAsync(nextToken, message, messageKind);
    }


    private void CloseSessionRenewalDialog(bool resetPromptToken = false)
    {
        IsSessionRenewalDialogOpen = false;
        _sessionRenewalDialogToken = Guid.Empty;
        _sessionRenewalDialogProfileName = string.Empty;

        if (resetPromptToken)
            _sessionRenewalPromptShownForToken = Guid.Empty;

        this.RaisePropertyChanged(nameof(SessionRenewalWarningMessage));
    }


    private string GetSessionDisplayName(Guid token)
    {
        var session = _authSessionRegistry.GetSession(token);
        if (!string.IsNullOrWhiteSpace(session?.DisplayName))
            return session.DisplayName;

        if (token == _authSessionRegistry.CurrentUserToken && !string.IsNullOrWhiteSpace(CurrentUserDisplayName))
            return CurrentUserDisplayName;

        return GetTranslation("Profiles_UnknownProfile");
    }


    private void SetSessionProfile(Guid token, UserProfileInfoResponse profile)
    {
        if (_authSessionRegistry.GetSession(token) is null)
            _authSessionRegistry.TryAdd(token);

        _authSessionRegistry.TrySetProfile(
            token,
            profile.UId,
            BuildDisplayName(profile),
            BuildSubtitle(profile),
            profile.Username,
            profile.Email,
            profile.IsRememberMeEnabled);
    }


    private async Task<bool> ContainsActiveUserIdAsync(Guid userId, Guid excludedToken = default)
    {
        if (_authSessionRegistry.ContainsUserId(userId, excludedToken))
            return true;

        foreach (var token in _authSessionRegistry.ListTokens().Where(token => token != excludedToken).ToList())
        {
            var session = _authSessionRegistry.GetSession(token);
            if (session?.UserId == userId)
                return true;

            try
            {
                var profile = await _endpoints.GetUserProfileInfoAsync(token);
                SetSessionProfile(token, profile);

                if (profile.UId == userId)
                    return true;
            }
            catch
            {
            }
        }

        return false;
    }

    private string GetSessionInvalidationMessage(AuthSessionInvalidationReason reason) =>
        reason switch
        {
            AuthSessionInvalidationReason.ProfilePasswordChanged => GetTranslation("Shell_ProfilePasswordChangedLoggedOut"),
            AuthSessionInvalidationReason.ProfileRemoved => GetTranslation("Shell_ProfileRemovedLoggedOut"),
            AuthSessionInvalidationReason.Expired => GetTranslation("Shell_SessionExpired"),
            _ => GetTranslation("Shell_LoggedOut")
        };


    private int CurrentMainPageIndex
    {
        get
        {
            if (ReferenceEquals(CurrentPageViewModel, PasswordsViewModel))
            {
                return 0;
            }

            if (ReferenceEquals(CurrentPageViewModel, ProfileViewModel) && ProfileViewModel.IsDevicesMainPage)
            {
                return 1;
            }

            if (ReferenceEquals(CurrentPageViewModel, ProfileViewModel))
            {
                return 2;
            }

            return 0;
        }
    }

    private void RaiseNavigationStateProperties()
    {
        this.RaisePropertyChanged(nameof(CurrentMobileNavigationIndex));
        this.RaisePropertyChanged(nameof(MobileCurrentPageLabel));
        this.RaisePropertyChanged(nameof(MobilePageIndicatorText));
        this.RaisePropertyChanged(nameof(IsPasswordsMainPageSelected));
        this.RaisePropertyChanged(nameof(IsDevicesMainPageSelected));
        this.RaisePropertyChanged(nameof(IsProfileMainPageSelected));
        this.RaisePropertyChanged(nameof(PasswordsNavigationFrameBrush));
        this.RaisePropertyChanged(nameof(DevicesNavigationFrameBrush));
        this.RaisePropertyChanged(nameof(ProfileNavigationFrameBrush));
        this.RaisePropertyChanged(nameof(IsDesktopNavigationVisible));
        this.RaisePropertyChanged(nameof(IsMobilePageIndicatorVisible));
    }


    private static string BuildSubtitle(UserProfileInfoResponse profile) =>
        string.IsNullOrWhiteSpace(profile.Username)
            ? profile.Email
            : $"@{profile.Username}";


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
