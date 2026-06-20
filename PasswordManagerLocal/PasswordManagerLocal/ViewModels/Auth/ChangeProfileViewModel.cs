using PasswordManagerLocal.Abstractions.Services;
using PasswordManagerLocal.Services;
using PasswordManagerLocalBackend.Abstractions;
using PasswordManagerLocalBackend.Responses;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;

namespace PasswordManagerLocal.ViewModels.Auth;

public sealed class ChangeProfileViewModel : ViewModelBase
{
    private readonly IEndpoints _endpoints;
    private readonly IAuthSessionRegistry _authSessionRegistry;
    private readonly Func<Task> _navigateBackAsync;
    private readonly Action _navigateToLoginAnotherProfile;
    private readonly Func<Guid, Task> _selectProfileAsync;

    private bool _isBusy;
    private bool _isStartupSelection;
    private string? _errorMessage;

    public ChangeProfileViewModel(
        UiPreferencesService uiPreferences,
        IEndpoints endpoints,
        IAuthSessionRegistry authSessionRegistry,
        Func<Task> navigateBackAsync,
        Action navigateToLoginAnotherProfile,
        Func<Guid, Task> selectProfileAsync)
        : base(uiPreferences)
    {
        _endpoints = endpoints;
        _authSessionRegistry = authSessionRegistry;
        _navigateBackAsync = navigateBackAsync;
        _navigateToLoginAnotherProfile = navigateToLoginAnotherProfile;
        _selectProfileAsync = selectProfileAsync;

        BackCommand = ReactiveCommand.CreateFromTask(_navigateBackAsync);
        RefreshCommand = ReactiveCommand.CreateFromTask(LoadAsync);
        LoginAnotherProfileCommand = ReactiveCommand.Create(_navigateToLoginAnotherProfile);
    }

    public ObservableCollection<ProfileSessionItemViewModel> Profiles { get; } = new();

    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public bool IsBackButtonVisible => !IsStartupSelection;

    public bool IsStartupSelection
    {
        get => _isStartupSelection;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isStartupSelection, value);
            this.RaisePropertyChanged(nameof(IsBackButtonVisible));
            this.RaisePropertyChanged(nameof(Title));
            this.RaisePropertyChanged(nameof(Subtitle));
            this.RaisePropertyChanged(nameof(LoggedInProfilesLabel));
            this.RaisePropertyChanged(nameof(EmptyProfilesTitle));
            this.RaisePropertyChanged(nameof(EmptyProfilesDescription));
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _errorMessage, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasProfiles => Profiles.Count > 0;

    public bool IsProfilesEmpty => Profiles.Count == 0;

    public ReactiveCommand<Unit, Unit> BackCommand { get; }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    public ReactiveCommand<Unit, Unit> LoginAnotherProfileCommand { get; }

    public string Title => IsStartupSelection
        ? GetTranslation("Profiles_Startup_Title")
        : GetTranslation("Profiles_Title");

    public string Subtitle => IsStartupSelection
        ? GetTranslation("Profiles_Startup_Subtitle")
        : GetTranslation("Profiles_Subtitle");

    public string BackLabel => GetTranslation("Common_Back");

    public string RefreshLabel => GetTranslation("Common_Refresh");

    public string LoginAnotherProfileLabel => GetTranslation("Profiles_LoginAnother");

    public string LoggedInProfilesLabel => IsStartupSelection
        ? GetTranslation("Profiles_RememberedProfiles")
        : GetTranslation("Profiles_LoggedInProfiles");

    public string EmptyProfilesTitle => IsStartupSelection
        ? GetTranslation("Profiles_Startup_Empty_Title")
        : GetTranslation("Profiles_Empty_Title");

    public string EmptyProfilesDescription => IsStartupSelection
        ? GetTranslation("Profiles_Startup_Empty_Description")
        : GetTranslation("Profiles_Empty_Description");

    public string BusyText => GetTranslation("Common_Loading");

    protected override void OnLanguageChanged()
    {
        this.RaisePropertyChanged(nameof(Title));
        this.RaisePropertyChanged(nameof(Subtitle));
        this.RaisePropertyChanged(nameof(BackLabel));
        this.RaisePropertyChanged(nameof(IsBackButtonVisible));
        this.RaisePropertyChanged(nameof(RefreshLabel));
        this.RaisePropertyChanged(nameof(LoginAnotherProfileLabel));
        this.RaisePropertyChanged(nameof(LoggedInProfilesLabel));
        this.RaisePropertyChanged(nameof(EmptyProfilesTitle));
        this.RaisePropertyChanged(nameof(EmptyProfilesDescription));
        this.RaisePropertyChanged(nameof(BusyText));
    }

    public void SetStartupSelectionMode(bool isStartupSelection)
    {
        IsStartupSelection = isStartupSelection;
    }


    public async Task LoadAsync()
    {
        if (IsBusy)
            return;

        ErrorMessage = null;
        IsBusy = true;
        Profiles.Clear();
        RaiseProfilesChanged();

        try
        {
            foreach (var originalToken in _authSessionRegistry.ListTokens().ToList())
            {
                var token = originalToken;
                var status = await _endpoints.GetAuthSessionStatusAsync(token);
                if (!status.IsAuthenticated)
                {
                    var session = _authSessionRegistry.GetSession(token);
                    if (session?.IsRememberMeEnabled == true && session.UserId != Guid.Empty)
                    {
                        try
                        {
                            var newToken = await _endpoints.InitializeRememberMeSessionAsync(session.UserId);
                            if (!_authSessionRegistry.TryReplaceToken(token, newToken))
                            {
                                _authSessionRegistry.TryAdd(newToken, originalToken == _authSessionRegistry.CurrentUserToken);
                                _authSessionRegistry.TryRemove(token);
                            }

                            token = newToken;
                            status = await _endpoints.GetAuthSessionStatusAsync(token);
                        }
                        catch
                        {
                        }
                    }

                    if (!status.IsAuthenticated)
                    {
                        _authSessionRegistry.TryRemove(originalToken);
                        continue;
                    }
                }

                var profile = await _endpoints.GetUserProfileInfoAsync(token);
                var displayName = BuildDisplayName(profile);
                var subtitle = string.IsNullOrWhiteSpace(profile.Username)
                    ? profile.Email
                    : $"@{profile.Username}";

                _authSessionRegistry.TrySetProfile(
                    token,
                    profile.UId,
                    displayName,
                    subtitle,
                    profile.Username,
                    profile.Email,
                    profile.IsRememberMeEnabled);

                Profiles.Add(new ProfileSessionItemViewModel(
                    UiPreferences,
                    token,
                    displayName,
                    subtitle,
                    token == _authSessionRegistry.CurrentUserToken,
                    SelectProfileAsync));
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = GetSafeErrorMessage(ex);
        }
        finally
        {
            IsBusy = false;
            RaiseProfilesChanged();
        }
    }

    public void RefreshCurrentSelection()
    {
        var current = _authSessionRegistry.CurrentUserToken;
        foreach (var profile in Profiles)
            profile.IsCurrent = profile.Token == current;
    }

    private async Task SelectProfileAsync(Guid token)
    {
        if (IsBusy || token == Guid.Empty)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await _selectProfileAsync(token);
            RefreshCurrentSelection();
        }
        catch (Exception ex)
        {
            ErrorMessage = GetSafeErrorMessage(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RaiseProfilesChanged()
    {
        this.RaisePropertyChanged(nameof(HasProfiles));
        this.RaisePropertyChanged(nameof(IsProfilesEmpty));
    }

    private static string BuildDisplayName(UserProfileInfoResponse profile)
    {
        var fullName = string.Join(
            " ",
            new[] { profile.LastName?.Trim(), profile.FirstName?.Trim() }
                .Where(part => !string.IsNullOrWhiteSpace(part)));

        if (!string.IsNullOrWhiteSpace(fullName))
            return fullName;

        if (!string.IsNullOrWhiteSpace(profile.Username))
            return profile.Username;

        return profile.Email;
    }
}
