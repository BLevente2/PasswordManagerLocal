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
    private bool _isSyncEnabled;
    private string _currentDeviceLabel = string.Empty;
    private string _blockedLabel = string.Empty;
    private string _trustedLabel = string.Empty;
    private string _notTrustedLabel = string.Empty;
    private string _syncEnabledLabel = string.Empty;
    private string _syncDisabledLabel = string.Empty;
    private string _syncToggleOnLabel = string.Empty;
    private string _syncToggleOffLabel = string.Empty;
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
        string currentDeviceLabel,
        string blockedLabel,
        string trustedLabel,
        string notTrustedLabel,
        string syncEnabledLabel,
        string syncDisabledLabel,
        string syncToggleOnLabel,
        string syncToggleOffLabel,
        string saveNameLabel,
        string unblockLabel,
        string disconnectLabel,
        string deviceNameLabel,
        string deviceLastSeenLabel,
        string deviceLastSyncLabel,
        string deviceLinkedAtLabel,
        string deviceBlockedReasonLabel,
        string deviceBlockedAtLabel,
        string deviceInvalidAttemptsLabel,
        Func<DeviceItemViewModel, Task> viewAsync,
        Func<DeviceItemViewModel, Task> saveNameAsync,
        Func<DeviceItemViewModel, Task> toggleSyncAsync,
        Func<DeviceItemViewModel, Task> unblockAsync,
        Action<DeviceItemViewModel> beginDisconnect)
    {
        DeviceId = device.DeviceId;
        Name = device.Name;
        DeviceName = device.DeviceName;
        EditableName = device.Name;
        LastSync = device.LastSync;
        LastSeen = device.LastSeen;
        IsTrusted = device.IsTrusted;
        IsBlocked = device.IsBlocked;
        BlockedReason = device.BlockedReason;
        BlockedAt = device.BlockedAt;
        InvalidSyncAttemptCount = device.InvalidSyncAttemptCount;
        IsSyncEnabled = device.IsSyncEnabled;
        LinkedAt = device.LinkedAt;
        IsCurrentDevice = device.IsCurrentDevice;
        _currentDeviceLabel = currentDeviceLabel;
        _blockedLabel = blockedLabel;
        _trustedLabel = trustedLabel;
        _notTrustedLabel = notTrustedLabel;
        _syncEnabledLabel = syncEnabledLabel;
        _syncDisabledLabel = syncDisabledLabel;
        _syncToggleOnLabel = syncToggleOnLabel;
        _syncToggleOffLabel = syncToggleOffLabel;
        _saveNameLabel = saveNameLabel;
        _unblockLabel = unblockLabel;
        _disconnectLabel = disconnectLabel;
        _deviceNameLabel = deviceNameLabel;
        _deviceLastSeenLabel = deviceLastSeenLabel;
        _deviceLastSyncLabel = deviceLastSyncLabel;
        _deviceLinkedAtLabel = deviceLinkedAtLabel;
        _deviceBlockedReasonLabel = deviceBlockedReasonLabel;
        _deviceBlockedAtLabel = deviceBlockedAtLabel;
        _deviceInvalidAttemptsLabel = deviceInvalidAttemptsLabel;
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

    public string Name { get; private set; }

    public string DeviceName { get; }

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

    public bool IsSyncEnabled
    {
        get => _isSyncEnabled;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isSyncEnabled, value);
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

    public string SyncStateText => IsSyncEnabled ? SyncEnabledLabel : SyncDisabledLabel;

    public string LastSyncText => LastSync.ToLocalTime().ToString("g");

    public string LastSeenText => LastSeen.ToLocalTime().ToString("g");

    public string LinkedAtText => LinkedAt.ToLocalTime().ToString("g");

    public string BlockedAtText => BlockedAt?.ToLocalTime().ToString("g") ?? string.Empty;

    public ReactiveCommand<Unit, Unit> ViewCommand { get; }

    public ReactiveCommand<Unit, Unit> SaveNameCommand { get; }

    public ReactiveCommand<Unit, Unit> ToggleSyncCommand { get; }

    public ReactiveCommand<Unit, Unit> UnblockCommand { get; }

    public ReactiveCommand<Unit, Unit> BeginDisconnectCommand { get; }

    public void ApplySyncState(bool isSyncEnabled) =>
        IsSyncEnabled = isSyncEnabled;

    public void ApplySavedName(string name)
    {
        Name = name;
        EditableName = name;
        this.RaisePropertyChanged(nameof(Name));
    }

    public void ApplyLocalization(
        string currentDeviceLabel,
        string blockedLabel,
        string trustedLabel,
        string notTrustedLabel,
        string syncEnabledLabel,
        string syncDisabledLabel,
        string syncToggleOnLabel,
        string syncToggleOffLabel,
        string saveNameLabel,
        string unblockLabel,
        string disconnectLabel,
        string deviceNameLabel,
        string deviceLastSeenLabel,
        string deviceLastSyncLabel,
        string deviceLinkedAtLabel,
        string deviceBlockedReasonLabel,
        string deviceBlockedAtLabel,
        string deviceInvalidAttemptsLabel)
    {
        _currentDeviceLabel = currentDeviceLabel;
        _blockedLabel = blockedLabel;
        _trustedLabel = trustedLabel;
        _notTrustedLabel = notTrustedLabel;
        _syncEnabledLabel = syncEnabledLabel;
        _syncDisabledLabel = syncDisabledLabel;
        _syncToggleOnLabel = syncToggleOnLabel;
        _syncToggleOffLabel = syncToggleOffLabel;
        _saveNameLabel = saveNameLabel;
        _unblockLabel = unblockLabel;
        _disconnectLabel = disconnectLabel;
        _deviceNameLabel = deviceNameLabel;
        _deviceLastSeenLabel = deviceLastSeenLabel;
        _deviceLastSyncLabel = deviceLastSyncLabel;
        _deviceLinkedAtLabel = deviceLinkedAtLabel;
        _deviceBlockedReasonLabel = deviceBlockedReasonLabel;
        _deviceBlockedAtLabel = deviceBlockedAtLabel;
        _deviceInvalidAttemptsLabel = deviceInvalidAttemptsLabel;

        this.RaisePropertyChanged(nameof(CurrentDeviceLabel));
        this.RaisePropertyChanged(nameof(BlockedLabel));
        this.RaisePropertyChanged(nameof(TrustedLabel));
        this.RaisePropertyChanged(nameof(NotTrustedLabel));
        this.RaisePropertyChanged(nameof(SyncEnabledLabel));
        this.RaisePropertyChanged(nameof(SyncDisabledLabel));
        this.RaisePropertyChanged(nameof(SyncToggleOnLabel));
        this.RaisePropertyChanged(nameof(SyncToggleOffLabel));
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
        string currentDeviceLabel,
        string blockedLabel,
        string trustedLabel,
        string notTrustedLabel,
        string syncEnabledLabel,
        string syncDisabledLabel,
        string syncToggleOnLabel,
        string syncToggleOffLabel,
        string saveNameLabel,
        string unblockLabel,
        string disconnectLabel,
        string deviceNameLabel,
        string deviceLastSeenLabel,
        string deviceLastSyncLabel,
        string deviceLinkedAtLabel,
        string deviceBlockedReasonLabel,
        string deviceBlockedAtLabel,
        string deviceInvalidAttemptsLabel,
        Func<DeviceItemViewModel, Task> viewAsync,
        Func<DeviceItemViewModel, Task> saveNameAsync,
        Func<DeviceItemViewModel, Task> toggleSyncAsync,
        Func<DeviceItemViewModel, Task> unblockAsync,
        Action<DeviceItemViewModel> beginDisconnect) =>
        new(
            device,
            currentDeviceLabel,
            blockedLabel,
            trustedLabel,
            notTrustedLabel,
            syncEnabledLabel,
            syncDisabledLabel,
            syncToggleOnLabel,
            syncToggleOffLabel,
            saveNameLabel,
            unblockLabel,
            disconnectLabel,
            deviceNameLabel,
            deviceLastSeenLabel,
            deviceLastSyncLabel,
            deviceLinkedAtLabel,
            deviceBlockedReasonLabel,
            deviceBlockedAtLabel,
            deviceInvalidAttemptsLabel,
            viewAsync,
            saveNameAsync,
            toggleSyncAsync,
            unblockAsync,
            beginDisconnect);
}
