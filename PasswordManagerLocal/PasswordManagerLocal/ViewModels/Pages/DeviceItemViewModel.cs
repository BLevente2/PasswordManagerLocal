using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Responses;
using ReactiveUI;
using System.Reactive;

namespace PasswordManagerLocal.ViewModels.Pages;

public sealed class DeviceItemViewModel : ReactiveObject
{
    private readonly Func<DeviceItemViewModel, Task> _viewAsync;
    private readonly Func<DeviceItemViewModel, Task> _saveNameAsync;
    private readonly Func<DeviceItemViewModel, Task> _toggleSyncAsync;
    private readonly Func<DeviceItemViewModel, Task> _unblockAsync;
    private readonly Action<DeviceItemViewModel> _beginDisconnect;
    private string _editableName = string.Empty;
    private bool _isSyncOn;
    private string _currentDeviceLabel = string.Empty;
    private string _blockedLabel = string.Empty;
    private string _trustedLabel = string.Empty;
    private string _notTrustedLabel = string.Empty;
    private string _syncEnabledLabel = string.Empty;
    private string _syncDisabledLabel = string.Empty;
    private string _syncToggleOnLabel = string.Empty;
    private string _syncToggleOffLabel = string.Empty;
    private string _windowsPcLabel = string.Empty;
    private string _androidMobileLabel = string.Empty;
    private string _unknownDeviceTypeLabel = string.Empty;
    private string _saveNameLabel = string.Empty;
    private string _unblockLabel = string.Empty;
    private string _disconnectLabel = string.Empty;
    private string _deviceNameLabel = string.Empty;
    private string _deviceLastSeenLabel = string.Empty;
    private string _deviceLastSyncLabel = string.Empty;
    private string _deviceLinkedAtLabel = string.Empty;
    private string _deviceBlockedReasonLabel = string.Empty;
    private string _deviceBlockedAtLabel = string.Empty;
    private string _deviceInvalidAttemptsLabel = string.Empty;

    private DeviceItemViewModel(
        UserDeviceInfoResponse device,
        DeviceItemLocalization localization,
        Func<DeviceItemViewModel, Task> viewAsync,
        Func<DeviceItemViewModel, Task> saveNameAsync,
        Func<DeviceItemViewModel, Task> toggleSyncAsync,
        Func<DeviceItemViewModel, Task> unblockAsync,
        Action<DeviceItemViewModel> beginDisconnect)
    {
        DeviceId = device.DeviceId;
        DeviceType = device.DeviceType;
        Name = device.Name;
        EditableName = device.Name;
        LastSync = device.LastSync;
        LastSeen = device.LastSeen;
        IsTrusted = device.IsTrusted;
        IsBlocked = device.IsBlocked;
        BlockedReason = device.BlockedReason;
        BlockedAt = device.BlockedAt;
        InvalidSyncAttemptCount = device.InvalidSyncAttemptCount;
        IsSyncOn = device.IsSyncOn;
        LinkedAt = device.LinkedAt;
        IsCurrentDevice = device.IsCurrentDevice;
        AssignLocalization(localization);
        _viewAsync = viewAsync;
        _saveNameAsync = saveNameAsync;
        _toggleSyncAsync = toggleSyncAsync;
        _unblockAsync = unblockAsync;
        _beginDisconnect = beginDisconnect;
        ViewCommand = ReactiveCommand.CreateFromTask(() => _viewAsync(this));
        SaveNameCommand = ReactiveCommand.CreateFromTask(() => _saveNameAsync(this));
        ToggleSyncCommand = ReactiveCommand.CreateFromTask(() => _toggleSyncAsync(this));
        UnblockCommand = ReactiveCommand.CreateFromTask(() => _unblockAsync(this));
        BeginDisconnectCommand = ReactiveCommand.Create(() => _beginDisconnect(this));
    }

    public Guid DeviceId { get; }

    public DeviceType DeviceType { get; }

    public string Name { get; private set; }

    public string EditableName
    {
        get => _editableName;
        set => this.RaiseAndSetIfChanged(ref _editableName, value);
    }

    public DateTime LastSync { get; }

    public DateTime LastSeen { get; }

    public bool IsTrusted { get; }

    public bool IsBlocked { get; }

    public string? BlockedReason { get; }

    public DateTimeOffset? BlockedAt { get; }

    public int InvalidSyncAttemptCount { get; }

    public bool IsSyncOn
    {
        get => _isSyncOn;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isSyncOn, value);
            this.RaisePropertyChanged(nameof(SyncStateText));
        }
    }

    public DateTimeOffset LinkedAt { get; }

    public bool IsCurrentDevice { get; }

    public bool CanDisconnect => !IsCurrentDevice;

    public bool CanUnblock => IsBlocked && !IsCurrentDevice;

    public bool HasBlockedReason => !string.IsNullOrWhiteSpace(BlockedReason);

    public bool HasBlockedAt => BlockedAt is not null;

    public bool HasInvalidSyncAttempts => InvalidSyncAttemptCount > 0;

    public string CurrentDeviceLabel => _currentDeviceLabel;

    public string BlockedLabel => _blockedLabel;

    public string TrustedLabel => _trustedLabel;

    public string NotTrustedLabel => _notTrustedLabel;

    public string SyncEnabledLabel => _syncEnabledLabel;

    public string SyncDisabledLabel => _syncDisabledLabel;

    public string SyncToggleOnLabel => _syncToggleOnLabel;

    public string SyncToggleOffLabel => _syncToggleOffLabel;

    public string DeviceTypeText => DeviceType switch
    {
        PasswordManagerLocalBackend.Models.DeviceType.WindowsPc => _windowsPcLabel,
        PasswordManagerLocalBackend.Models.DeviceType.AndroidMobile => _androidMobileLabel,
        _ => _unknownDeviceTypeLabel
    };

    public string SaveNameLabel => _saveNameLabel;

    public string UnblockLabel => _unblockLabel;

    public string DisconnectLabel => _disconnectLabel;

    public string DeviceNameLabel => _deviceNameLabel;

    public string DeviceLastSeenLabel => _deviceLastSeenLabel;

    public string DeviceLastSyncLabel => _deviceLastSyncLabel;

    public string DeviceLinkedAtLabel => _deviceLinkedAtLabel;

    public string DeviceBlockedReasonLabel => _deviceBlockedReasonLabel;

    public string DeviceBlockedAtLabel => _deviceBlockedAtLabel;

    public string DeviceInvalidAttemptsLabel => _deviceInvalidAttemptsLabel;

    public string TrustStateText => IsTrusted ? TrustedLabel : NotTrustedLabel;

    public string SyncStateText => IsSyncOn ? SyncEnabledLabel : SyncDisabledLabel;

    public string LastSyncText => LastSync.ToLocalTime().ToString("g");

    public string LastSeenText => LastSeen.ToLocalTime().ToString("g");

    public string LinkedAtText => LinkedAt.ToLocalTime().ToString("g");

    public string BlockedAtText => BlockedAt?.ToLocalTime().ToString("g") ?? string.Empty;

    public ReactiveCommand<Unit, Unit> ViewCommand { get; }

    public ReactiveCommand<Unit, Unit> SaveNameCommand { get; }

    public ReactiveCommand<Unit, Unit> ToggleSyncCommand { get; }

    public ReactiveCommand<Unit, Unit> UnblockCommand { get; }

    public ReactiveCommand<Unit, Unit> BeginDisconnectCommand { get; }

    public void ApplySyncState(bool isSyncOn) =>
        IsSyncOn = isSyncOn;

    public void ApplySavedName(string name)
    {
        Name = name;
        EditableName = name;
        this.RaisePropertyChanged(nameof(Name));
    }

    public void ApplyLocalization(DeviceItemLocalization localization)
    {
        AssignLocalization(localization);
        RaiseLocalizedPropertiesChanged();
    }

    private void AssignLocalization(DeviceItemLocalization localization)
    {
        _currentDeviceLabel = localization.CurrentDeviceLabel;
        _blockedLabel = localization.BlockedLabel;
        _trustedLabel = localization.TrustedLabel;
        _notTrustedLabel = localization.NotTrustedLabel;
        _syncEnabledLabel = localization.SyncEnabledLabel;
        _syncDisabledLabel = localization.SyncDisabledLabel;
        _syncToggleOnLabel = localization.SyncToggleOnLabel;
        _syncToggleOffLabel = localization.SyncToggleOffLabel;
        _windowsPcLabel = localization.WindowsPcLabel;
        _androidMobileLabel = localization.AndroidMobileLabel;
        _unknownDeviceTypeLabel = localization.UnknownDeviceTypeLabel;
        _saveNameLabel = localization.SaveNameLabel;
        _unblockLabel = localization.UnblockLabel;
        _disconnectLabel = localization.DisconnectLabel;
        _deviceNameLabel = localization.DeviceNameLabel;
        _deviceLastSeenLabel = localization.DeviceLastSeenLabel;
        _deviceLastSyncLabel = localization.DeviceLastSyncLabel;
        _deviceLinkedAtLabel = localization.DeviceLinkedAtLabel;
        _deviceBlockedReasonLabel = localization.DeviceBlockedReasonLabel;
        _deviceBlockedAtLabel = localization.DeviceBlockedAtLabel;
        _deviceInvalidAttemptsLabel = localization.DeviceInvalidAttemptsLabel;
    }

    private void RaiseLocalizedPropertiesChanged()
    {
        this.RaisePropertyChanged(nameof(CurrentDeviceLabel));
        this.RaisePropertyChanged(nameof(BlockedLabel));
        this.RaisePropertyChanged(nameof(TrustedLabel));
        this.RaisePropertyChanged(nameof(NotTrustedLabel));
        this.RaisePropertyChanged(nameof(SyncEnabledLabel));
        this.RaisePropertyChanged(nameof(SyncDisabledLabel));
        this.RaisePropertyChanged(nameof(SyncToggleOnLabel));
        this.RaisePropertyChanged(nameof(SyncToggleOffLabel));
        this.RaisePropertyChanged(nameof(DeviceTypeText));
        this.RaisePropertyChanged(nameof(SaveNameLabel));
        this.RaisePropertyChanged(nameof(UnblockLabel));
        this.RaisePropertyChanged(nameof(DisconnectLabel));
        this.RaisePropertyChanged(nameof(DeviceNameLabel));
        this.RaisePropertyChanged(nameof(DeviceLastSeenLabel));
        this.RaisePropertyChanged(nameof(DeviceLastSyncLabel));
        this.RaisePropertyChanged(nameof(DeviceLinkedAtLabel));
        this.RaisePropertyChanged(nameof(DeviceBlockedReasonLabel));
        this.RaisePropertyChanged(nameof(DeviceBlockedAtLabel));
        this.RaisePropertyChanged(nameof(DeviceInvalidAttemptsLabel));
        this.RaisePropertyChanged(nameof(TrustStateText));
        this.RaisePropertyChanged(nameof(SyncStateText));
    }

    public static DeviceItemViewModel Create(
        UserDeviceInfoResponse device,
        DeviceItemLocalization localization,
        Func<DeviceItemViewModel, Task> viewAsync,
        Func<DeviceItemViewModel, Task> saveNameAsync,
        Func<DeviceItemViewModel, Task> toggleSyncAsync,
        Func<DeviceItemViewModel, Task> unblockAsync,
        Action<DeviceItemViewModel> beginDisconnect) =>
        new(
            device,
            localization,
            viewAsync,
            saveNameAsync,
            toggleSyncAsync,
            unblockAsync,
            beginDisconnect);

}
