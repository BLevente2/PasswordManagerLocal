using PasswordManagerLocalBackend.Responses;
using ReactiveUI;
using System.Reactive;

namespace PasswordManagerLocal.ViewModels.Pages;

public sealed class DeviceItemViewModel : ReactiveObject
{
    private readonly Func<DeviceItemViewModel, Task> _saveNameAsync;
    private readonly Func<DeviceItemViewModel, Task> _toggleSyncAsync;
    private readonly Func<DeviceItemViewModel, Task> _unblockAsync;
    private readonly Action<DeviceItemViewModel> _beginDisconnect;
    private string _editableName;
    private bool _isSyncEnabled;

    private DeviceItemViewModel(
        UserDeviceInfoResponse device,
        string currentDeviceLabel,
        string blockedLabel,
        string trustedLabel,
        string notTrustedLabel,
        string syncEnabledLabel,
        string syncDisabledLabel,
        string saveNameLabel,
        string unblockLabel,
        string disconnectLabel,
        string deviceNameLabel,
        string deviceFingerprintLabel,
        string deviceLastSeenLabel,
        string deviceLastSyncLabel,
        string deviceLinkedAtLabel,
        string deviceBlockedReasonLabel,
        string deviceBlockedAtLabel,
        string deviceInvalidAttemptsLabel,
        Func<DeviceItemViewModel, Task> saveNameAsync,
        Func<DeviceItemViewModel, Task> toggleSyncAsync,
        Func<DeviceItemViewModel, Task> unblockAsync,
        Action<DeviceItemViewModel> beginDisconnect)
    {
        DeviceId = device.DeviceId;
        Name = device.Name;
        DeviceName = device.DeviceName;
        EditableName = device.Name;
        TlsCertFingerprint = device.TlsCertFingerprint;
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
        CurrentDeviceLabel = currentDeviceLabel;
        BlockedLabel = blockedLabel;
        TrustedLabel = trustedLabel;
        NotTrustedLabel = notTrustedLabel;
        SyncEnabledLabel = syncEnabledLabel;
        SyncDisabledLabel = syncDisabledLabel;
        SaveNameLabel = saveNameLabel;
        UnblockLabel = unblockLabel;
        DisconnectLabel = disconnectLabel;
        DeviceNameLabel = deviceNameLabel;
        DeviceFingerprintLabel = deviceFingerprintLabel;
        DeviceLastSeenLabel = deviceLastSeenLabel;
        DeviceLastSyncLabel = deviceLastSyncLabel;
        DeviceLinkedAtLabel = deviceLinkedAtLabel;
        DeviceBlockedReasonLabel = deviceBlockedReasonLabel;
        DeviceBlockedAtLabel = deviceBlockedAtLabel;
        DeviceInvalidAttemptsLabel = deviceInvalidAttemptsLabel;
        _saveNameAsync = saveNameAsync;
        _toggleSyncAsync = toggleSyncAsync;
        _unblockAsync = unblockAsync;
        _beginDisconnect = beginDisconnect;

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

    public string TlsCertFingerprint { get; }

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

    public string CurrentDeviceLabel { get; }

    public string BlockedLabel { get; }

    public string TrustedLabel { get; }

    public string NotTrustedLabel { get; }

    public string SyncEnabledLabel { get; }

    public string SyncDisabledLabel { get; }

    public string SaveNameLabel { get; }

    public string UnblockLabel { get; }

    public string DisconnectLabel { get; }

    public string DeviceNameLabel { get; }

    public string DeviceFingerprintLabel { get; }

    public string DeviceLastSeenLabel { get; }

    public string DeviceLastSyncLabel { get; }

    public string DeviceLinkedAtLabel { get; }

    public string DeviceBlockedReasonLabel { get; }

    public string DeviceBlockedAtLabel { get; }

    public string DeviceInvalidAttemptsLabel { get; }

    public string TrustStateText => IsTrusted ? TrustedLabel : NotTrustedLabel;

    public string SyncStateText => IsSyncEnabled ? SyncEnabledLabel : SyncDisabledLabel;

    public string LastSyncText => LastSync.ToLocalTime().ToString("g");

    public string LastSeenText => LastSeen.ToLocalTime().ToString("g");

    public string LinkedAtText => LinkedAt.ToLocalTime().ToString("g");

    public string BlockedAtText => BlockedAt?.ToLocalTime().ToString("g") ?? string.Empty;

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

    public static DeviceItemViewModel Create(
        UserDeviceInfoResponse device,
        string currentDeviceLabel,
        string blockedLabel,
        string trustedLabel,
        string notTrustedLabel,
        string syncEnabledLabel,
        string syncDisabledLabel,
        string saveNameLabel,
        string unblockLabel,
        string disconnectLabel,
        string deviceNameLabel,
        string deviceFingerprintLabel,
        string deviceLastSeenLabel,
        string deviceLastSyncLabel,
        string deviceLinkedAtLabel,
        string deviceBlockedReasonLabel,
        string deviceBlockedAtLabel,
        string deviceInvalidAttemptsLabel,
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
            saveNameLabel,
            unblockLabel,
            disconnectLabel,
            deviceNameLabel,
            deviceFingerprintLabel,
            deviceLastSeenLabel,
            deviceLastSyncLabel,
            deviceLinkedAtLabel,
            deviceBlockedReasonLabel,
            deviceBlockedAtLabel,
            deviceInvalidAttemptsLabel,
            saveNameAsync,
            toggleSyncAsync,
            unblockAsync,
            beginDisconnect);
}
