using PasswordManagerLocal.Helpers;
using PasswordManagerLocal.Services;
using PasswordManagerLocalBackend.Abstractions;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;
using ReactiveUI;
using System.Collections.ObjectModel;
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
    private string _disconnectDevicePassword = string.Empty;
    private string? _statusMessage;
    private DeviceItemViewModel? _deviceToDisconnect;
    private DeviceItemViewModel? _pendingLocalSyncDevice;
    private bool _isDeviceDisconnectDialogOpen;
    private bool _isLocalSyncDialogOpen;
    private bool _isAddDeviceDialogOpen;
    private bool _pendingLocalSyncEnabled;
    private bool _isAddingDevice;
    private string _deviceEnrollmentCodeInput = string.Empty;

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

        Devices = [];

        SaveProfileCommand = ReactiveCommand.CreateFromTask(SaveProfileAsync);
        ChangeUsernameCommand = ReactiveCommand.CreateFromTask(ChangeUsernameAsync);
        ChangeMasterPasswordCommand = ReactiveCommand.CreateFromTask(ChangeMasterPasswordAsync);
        DeleteAccountCommand = ReactiveCommand.CreateFromTask(DeleteAccountAsync);
        RefreshDevicesCommand = ReactiveCommand.CreateFromTask(RefreshDevicesAsync);
        ConfirmDisconnectDeviceCommand = ReactiveCommand.CreateFromTask(ConfirmDisconnectDeviceAsync);
        CancelDisconnectDeviceCommand = ReactiveCommand.Create(CancelDisconnectDevice);
        ConfirmLocalSyncToggleCommand = ReactiveCommand.CreateFromTask(ConfirmLocalSyncToggleAsync);
        CancelLocalSyncToggleCommand = ReactiveCommand.Create(CancelLocalSyncToggle);
        BeginAddDeviceCommand = ReactiveCommand.CreateFromTask(BeginAddDeviceAsync);
        ConfirmAddDeviceCommand = ReactiveCommand.CreateFromTask(ConfirmAddDeviceAsync);
        CancelAddDeviceCommand = ReactiveCommand.Create(CancelAddDevice);
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

    public string DisconnectDevicePassword
    {
        get => _disconnectDevicePassword;
        set => this.RaiseAndSetIfChanged(ref _disconnectDevicePassword, value);
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

    public ObservableCollection<DeviceItemViewModel> Devices { get; }

    public bool HasDevices => Devices.Count > 0;

    public bool IsDevicesEmpty => Devices.Count == 0;

    public bool IsDeviceDisconnectDialogOpen
    {
        get => _isDeviceDisconnectDialogOpen;
        private set => this.RaiseAndSetIfChanged(ref _isDeviceDisconnectDialogOpen, value);
    }

    public bool IsLocalSyncDialogOpen
    {
        get => _isLocalSyncDialogOpen;
        private set => this.RaiseAndSetIfChanged(ref _isLocalSyncDialogOpen, value);
    }

    public bool IsAddDeviceDialogOpen
    {
        get => _isAddDeviceDialogOpen;
        private set => this.RaiseAndSetIfChanged(ref _isAddDeviceDialogOpen, value);
    }

    public bool IsAddingDevice
    {
        get => _isAddingDevice;
        private set => this.RaiseAndSetIfChanged(ref _isAddingDevice, value);
    }

    public string DeviceEnrollmentCodeInput
    {
        get => _deviceEnrollmentCodeInput;
        set => this.RaiseAndSetIfChanged(ref _deviceEnrollmentCodeInput, value);
    }

    public DeviceItemViewModel? DeviceToDisconnect
    {
        get => _deviceToDisconnect;
        private set
        {
            this.RaiseAndSetIfChanged(ref _deviceToDisconnect, value);
            this.RaisePropertyChanged(nameof(DisconnectDeviceName));
        }
    }

    public DeviceItemViewModel? PendingLocalSyncDevice
    {
        get => _pendingLocalSyncDevice;
        private set
        {
            this.RaiseAndSetIfChanged(ref _pendingLocalSyncDevice, value);
            this.RaisePropertyChanged(nameof(LocalSyncDeviceName));
        }
    }

    public bool PendingLocalSyncEnabled
    {
        get => _pendingLocalSyncEnabled;
        private set
        {
            this.RaiseAndSetIfChanged(ref _pendingLocalSyncEnabled, value);
            this.RaisePropertyChanged(nameof(LocalSyncConfirmLabel));
        }
    }

    public string DisconnectDeviceName => DeviceToDisconnect?.Name ?? string.Empty;

    public string LocalSyncDeviceName => PendingLocalSyncDevice?.Name ?? string.Empty;

    public string RegistrationDateText => RegistrationDate.ToLocalTime().ToString("f");

    public string LastLoginDateText => LastLoginDate.ToLocalTime().ToString("f");

    public ReactiveCommand<Unit, Unit> SaveProfileCommand { get; }

    public ReactiveCommand<Unit, Unit> ChangeUsernameCommand { get; }

    public ReactiveCommand<Unit, Unit> ChangeMasterPasswordCommand { get; }

    public ReactiveCommand<Unit, Unit> DeleteAccountCommand { get; }

    public ReactiveCommand<Unit, Unit> RefreshDevicesCommand { get; }

    public ReactiveCommand<Unit, Unit> ConfirmDisconnectDeviceCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelDisconnectDeviceCommand { get; }

    public ReactiveCommand<Unit, Unit> ConfirmLocalSyncToggleCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelLocalSyncToggleCommand { get; }

    public ReactiveCommand<Unit, Unit> BeginAddDeviceCommand { get; }

    public ReactiveCommand<Unit, Unit> ConfirmAddDeviceCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelAddDeviceCommand { get; }

    public string Title => GetTranslation("Profile_Title");

    public string Subtitle => GetTranslation("Profile_Subtitle");

    public string AccountOverviewLabel => GetTranslation("Profile_Overview_Title");

    public string PersonalInfoTitle => GetTranslation("Profile_Personal_Title");

    public string UsernameTitle => GetTranslation("Profile_Username_Title");

    public string SecurityTitle => GetTranslation("Profile_Security_Title");

    public string DevicesTitle => GetTranslation("Profile_Devices_Title");

    public string DevicesDescription => GetTranslation("Profile_Devices_Description");

    public string DevicesEmptyTitle => GetTranslation("Profile_Devices_Empty_Title");

    public string DevicesEmptyDescription => GetTranslation("Profile_Devices_Empty_Description");

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

    public string RefreshDevicesLabel => GetTranslation("Common_Refresh");

    public string AddDeviceLabel => GetTranslation("Profile_Device_Add");

    public string AddDeviceDialogTitle => GetTranslation("Profile_Device_Add_Title");

    public string AddDeviceDescription => GetTranslation("Profile_Device_Add_Description");

    public string AddDeviceCodeLabel => GetTranslation("Profile_Device_Add_Code_Label");

    public string AddDeviceCodePlaceholder => GetTranslation("Profile_Device_Add_Code_Placeholder");

    public string ConfirmAddDeviceLabel => GetTranslation("Profile_Device_Add_Confirm");

    public string CurrentDeviceLabel => GetTranslation("Profile_Device_Current");

    public string BlockedLabel => GetTranslation("Profile_Device_Blocked");

    public string TrustedLabel => GetTranslation("Profile_Device_Trusted");

    public string NotTrustedLabel => GetTranslation("Profile_Device_NotTrusted");

    public string SyncEnabledLabel => GetTranslation("Profile_Device_SyncEnabled");

    public string SyncDisabledLabel => GetTranslation("Profile_Device_SyncDisabled");

    public string SaveDeviceNameLabel => GetTranslation("Profile_Device_SaveName");

    public string UnblockDeviceLabel => GetTranslation("Profile_Device_Unblock");

    public string DisconnectDeviceLabel => GetTranslation("Profile_Device_Disconnect");

    public string DeviceNameLabel => GetTranslation("Profile_Device_Name");

    public string DeviceFingerprintLabel => GetTranslation("Profile_Device_Fingerprint");

    public string DeviceLastSeenLabel => GetTranslation("Profile_Device_LastSeen");

    public string DeviceLastSyncLabel => GetTranslation("Profile_Device_LastSync");

    public string DeviceLinkedAtLabel => GetTranslation("Profile_Device_LinkedAt");

    public string DeviceBlockedReasonLabel => GetTranslation("Profile_Device_BlockedReason");

    public string DeviceBlockedAtLabel => GetTranslation("Profile_Device_BlockedAt");

    public string DeviceInvalidAttemptsLabel => GetTranslation("Profile_Device_InvalidAttempts");

    public string DisconnectDialogTitle => GetTranslation("Profile_Device_Disconnect_Title");

    public string DisconnectDialogWarning => GetTranslation("Profile_Device_Disconnect_Warning");

    public string DisconnectDialogPasswordPlaceholder => GetTranslation("Profile_Device_Disconnect_Password_Placeholder");

    public string ConfirmDisconnectLabel => GetTranslation("Profile_Device_Disconnect_Confirm");

    public string CancelLabel => GetTranslation("Common_Cancel");

    public string LocalSyncDialogTitle => GetTranslation("Profile_LocalSync_Title");

    public string LocalSyncDialogWarning => GetTranslation("Profile_LocalSync_Warning");

    public string LocalSyncConfirmLabel => PendingLocalSyncEnabled
        ? GetTranslation("Profile_LocalSync_TurnOn")
        : GetTranslation("Profile_LocalSync_TurnOff");

    protected override void OnLanguageChanged()
    {
        this.RaisePropertyChanged(nameof(Title));
        this.RaisePropertyChanged(nameof(Subtitle));
        this.RaisePropertyChanged(nameof(AccountOverviewLabel));
        this.RaisePropertyChanged(nameof(PersonalInfoTitle));
        this.RaisePropertyChanged(nameof(UsernameTitle));
        this.RaisePropertyChanged(nameof(SecurityTitle));
        this.RaisePropertyChanged(nameof(DevicesTitle));
        this.RaisePropertyChanged(nameof(DevicesDescription));
        this.RaisePropertyChanged(nameof(DevicesEmptyTitle));
        this.RaisePropertyChanged(nameof(DevicesEmptyDescription));
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
        this.RaisePropertyChanged(nameof(RefreshDevicesLabel));
        this.RaisePropertyChanged(nameof(AddDeviceLabel));
        this.RaisePropertyChanged(nameof(AddDeviceDialogTitle));
        this.RaisePropertyChanged(nameof(AddDeviceDescription));
        this.RaisePropertyChanged(nameof(AddDeviceCodeLabel));
        this.RaisePropertyChanged(nameof(AddDeviceCodePlaceholder));
        this.RaisePropertyChanged(nameof(ConfirmAddDeviceLabel));
        this.RaisePropertyChanged(nameof(CurrentDeviceLabel));
        this.RaisePropertyChanged(nameof(BlockedLabel));
        this.RaisePropertyChanged(nameof(TrustedLabel));
        this.RaisePropertyChanged(nameof(NotTrustedLabel));
        this.RaisePropertyChanged(nameof(SyncEnabledLabel));
        this.RaisePropertyChanged(nameof(SyncDisabledLabel));
        this.RaisePropertyChanged(nameof(SaveDeviceNameLabel));
        this.RaisePropertyChanged(nameof(UnblockDeviceLabel));
        this.RaisePropertyChanged(nameof(DisconnectDeviceLabel));
        this.RaisePropertyChanged(nameof(DeviceNameLabel));
        this.RaisePropertyChanged(nameof(DeviceFingerprintLabel));
        this.RaisePropertyChanged(nameof(DeviceLastSeenLabel));
        this.RaisePropertyChanged(nameof(DeviceLastSyncLabel));
        this.RaisePropertyChanged(nameof(DeviceLinkedAtLabel));
        this.RaisePropertyChanged(nameof(DeviceBlockedReasonLabel));
        this.RaisePropertyChanged(nameof(DeviceBlockedAtLabel));
        this.RaisePropertyChanged(nameof(DeviceInvalidAttemptsLabel));
        this.RaisePropertyChanged(nameof(DisconnectDialogTitle));
        this.RaisePropertyChanged(nameof(DisconnectDialogWarning));
        this.RaisePropertyChanged(nameof(DisconnectDialogPasswordPlaceholder));
        this.RaisePropertyChanged(nameof(ConfirmDisconnectLabel));
        this.RaisePropertyChanged(nameof(CancelLabel));
        this.RaisePropertyChanged(nameof(LocalSyncDialogTitle));
        this.RaisePropertyChanged(nameof(LocalSyncDialogWarning));
        this.RaisePropertyChanged(nameof(LocalSyncConfirmLabel));
        this.RaisePropertyChanged(nameof(RegistrationDateText));
        this.RaisePropertyChanged(nameof(LastLoginDateText));
    }

    public async Task LoadAsync(Guid token, UserProfileInfoResponse profile)
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
        DisconnectDevicePassword = string.Empty;
        StatusMessage = null;
        CancelDisconnectDevice();
        CancelLocalSyncToggle();
        CancelAddDevice();
        await LoadDevicesAsync();
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
        DisconnectDevicePassword = string.Empty;
        StatusMessage = null;
        Devices.Clear();
        RaiseDeviceCollectionStateChanged();
        CancelDisconnectDevice();
        CancelLocalSyncToggle();
        CancelAddDevice();
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

    private async Task RefreshDevicesAsync() =>
        await LoadDevicesAsync();

    private async Task LoadDevicesAsync()
    {
        if (_token == Guid.Empty)
            return;

        try
        {
            var devices = await _endpoints.GetUserDevicesAsync(_token);
            Devices.Clear();

            foreach (var device in devices)
                Devices.Add(CreateDeviceItem(device));

            RaiseDeviceCollectionStateChanged();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private DeviceItemViewModel CreateDeviceItem(UserDeviceInfoResponse device) =>
        DeviceItemViewModel.Create(
            device,
            CurrentDeviceLabel,
            BlockedLabel,
            TrustedLabel,
            NotTrustedLabel,
            SyncEnabledLabel,
            SyncDisabledLabel,
            SaveDeviceNameLabel,
            UnblockDeviceLabel,
            DisconnectDeviceLabel,
            DeviceNameLabel,
            DeviceFingerprintLabel,
            DeviceLastSeenLabel,
            DeviceLastSyncLabel,
            DeviceLinkedAtLabel,
            DeviceBlockedReasonLabel,
            DeviceBlockedAtLabel,
            DeviceInvalidAttemptsLabel,
            SaveDeviceNameAsync,
            ToggleDeviceSyncAsync,
            UnblockDeviceAsync,
            BeginDisconnectDevice);

    private async Task SaveDeviceNameAsync(DeviceItemViewModel device)
    {
        if (_token == Guid.Empty)
            return;

        var normalizedName = device.EditableName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            StatusMessage = GetTranslation("Profile_Device_NameRequired");
            return;
        }

        try
        {
            if (device.IsCurrentDevice)
                await _endpoints.SetLocalDeviceNameAsync(_token, normalizedName);
            else
                await _endpoints.SetUserDeviceNameAsync(_token, device.DeviceId, normalizedName);

            device.ApplySavedName(normalizedName);
            await LoadDevicesAsync();
            StatusMessage = GetTranslation("Profile_Device_NameSaved");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task ToggleDeviceSyncAsync(DeviceItemViewModel device)
    {
        if (_token == Guid.Empty)
            return;

        var targetState = !device.IsSyncEnabled;

        if (device.IsCurrentDevice)
        {
            PendingLocalSyncDevice = device;
            PendingLocalSyncEnabled = targetState;
            IsLocalSyncDialogOpen = true;
            return;
        }

        try
        {
            await _endpoints.SetUserDeviceSyncEnabledAsync(_token, device.DeviceId, targetState);
            device.ApplySyncState(targetState);
            await LoadDevicesAsync();
            StatusMessage = targetState
                ? GetTranslation("Profile_Device_SyncTurnedOn")
                : GetTranslation("Profile_Device_SyncTurnedOff");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task UnblockDeviceAsync(DeviceItemViewModel device)
    {
        if (_token == Guid.Empty)
            return;

        try
        {
            await _endpoints.UnblockUserDeviceAsync(_token, device.DeviceId);
            await LoadDevicesAsync();
            StatusMessage = GetTranslation("Profile_Device_Unblocked");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void BeginDisconnectDevice(DeviceItemViewModel device)
    {
        if (!device.CanDisconnect)
            return;

        DeviceToDisconnect = device;
        DisconnectDevicePassword = string.Empty;
        IsDeviceDisconnectDialogOpen = true;
    }

    private void CancelDisconnectDevice()
    {
        IsDeviceDisconnectDialogOpen = false;
        DeviceToDisconnect = null;
        DisconnectDevicePassword = string.Empty;
    }

    private async Task ConfirmDisconnectDeviceAsync()
    {
        if (_token == Guid.Empty || DeviceToDisconnect is null)
            return;

        if (string.IsNullOrWhiteSpace(DisconnectDevicePassword))
        {
            StatusMessage = GetTranslation("Validation_Password_Required");
            return;
        }

        var passwordHash = SecretTransform.HashPassword(DisconnectDevicePassword);

        try
        {
            await _endpoints.DisconnectUserDeviceAsync(_token, DeviceToDisconnect.DeviceId, passwordHash);
            CancelDisconnectDevice();
            await LoadDevicesAsync();
            StatusMessage = GetTranslation("Profile_Device_Disconnected");
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

    private void CancelLocalSyncToggle()
    {
        IsLocalSyncDialogOpen = false;
        PendingLocalSyncDevice = null;
        PendingLocalSyncEnabled = false;
    }

    private async Task ConfirmLocalSyncToggleAsync()
    {
        if (PendingLocalSyncDevice is null)
            return;

        var targetState = PendingLocalSyncEnabled;

        try
        {
            await _endpoints.SetLocalDeviceSyncEnabledAsync(targetState);
            PendingLocalSyncDevice.ApplySyncState(targetState);
            CancelLocalSyncToggle();
            await LoadDevicesAsync();
            StatusMessage = targetState
                ? GetTranslation("Profile_LocalSync_OnSuccess")
                : GetTranslation("Profile_LocalSync_OffSuccess");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task BeginAddDeviceAsync()
    {
        if (_token == Guid.Empty)
            return;

        try
        {
            var isLocalSyncOn = await _endpoints.GetLocalDeviceSyncEnabledAsync();
            if (!isLocalSyncOn)
            {
                StatusMessage = GetTranslation("Profile_Device_AddSyncDisabled");
                return;
            }

            DeviceEnrollmentCodeInput = string.Empty;
            IsAddDeviceDialogOpen = true;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void CancelAddDevice()
    {
        IsAddDeviceDialogOpen = false;
        DeviceEnrollmentCodeInput = string.Empty;
        IsAddingDevice = false;
    }

    private async Task ConfirmAddDeviceAsync()
    {
        if (_token == Guid.Empty || IsAddingDevice)
            return;

        var code = DeviceEnrollmentCodeInput.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            StatusMessage = GetTranslation("Profile_Device_Add_CodeRequired");
            return;
        }

        try
        {
            IsAddingDevice = true;
            await _endpoints.AddDeviceByCodeAsync(_token, code);
            CancelAddDevice();
            await LoadDevicesAsync();
            StatusMessage = GetTranslation("Profile_Device_AddSuccess");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsAddingDevice = false;
        }
    }


    private void RaiseDeviceCollectionStateChanged()
    {
        this.RaisePropertyChanged(nameof(HasDevices));
        this.RaisePropertyChanged(nameof(IsDevicesEmpty));
    }
}
