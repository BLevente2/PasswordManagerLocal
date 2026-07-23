using PasswordManagerLocal.Backend.Hosting;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Runtime.Abstractions;
using PasswordManagerLocal.Windows.Agent.Backend;
using PasswordManagerLocal.Windows.Agent.DatabaseReset;
using PasswordManagerLocal.Windows.Agent.Endpoint;
using PasswordManagerLocal.Windows.Agent.Hosting;
using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Lifecycle;
using PasswordManagerLocal.Windows.Ipc.Server;

namespace PasswordManagerLocal.Windows.Agent.Status;

public sealed class WindowsAgentStatusProvider : IWindowsIpcStatusProvider
{
    private readonly IWindowsAgentStateSource _stateSource;
    private readonly IUiConnectionCoordinator _uiCoordinator;
    private readonly IWindowsBackgroundSyncSettingsReader _settingsReader;
    private readonly IWindowsAgentBackendRuntimeOwner _backendOwner;
    private readonly IWindowsAgentEndpointHost _endpointHost;
    private readonly AgentInteractiveEndpointAdapter _endpointAdapter;
    private readonly IWindowsAgentDatabaseResetCoordinator _resetCoordinator;

    public WindowsAgentStatusProvider(
        IWindowsAgentStateSource stateSource,
        IUiConnectionCoordinator uiCoordinator,
        IWindowsBackgroundSyncSettingsReader settingsReader,
        IWindowsAgentBackendRuntimeOwner backendOwner,
        IWindowsAgentEndpointHost endpointHost,
        AgentInteractiveEndpointAdapter endpointAdapter,
        IWindowsAgentDatabaseResetCoordinator resetCoordinator)
    {
        _stateSource = stateSource ?? throw new ArgumentNullException(nameof(stateSource));
        _uiCoordinator = uiCoordinator ?? throw new ArgumentNullException(nameof(uiCoordinator));
        _settingsReader = settingsReader ?? throw new ArgumentNullException(nameof(settingsReader));
        _backendOwner = backendOwner ?? throw new ArgumentNullException(nameof(backendOwner));
        _endpointHost = endpointHost ?? throw new ArgumentNullException(nameof(endpointHost));
        _endpointAdapter = endpointAdapter ?? throw new ArgumentNullException(nameof(endpointAdapter));
        _resetCoordinator = resetCoordinator ?? throw new ArgumentNullException(nameof(resetCoordinator));
    }

    public async Task<AgentStatusDto> GetAgentStatusAsync(CancellationToken cancellationToken)
    {
        bool backgroundSyncEnabled;
        try
        {
            backgroundSyncEnabled = await _settingsReader.ReadIsEnabledAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            backgroundSyncEnabled = false;
        }

        var owner = _backendOwner.Snapshot;
        var runtimeRunning = owner.Runtime.State is
            BackendRuntimeState.Starting or
            BackendRuntimeState.Ready or
            BackendRuntimeState.WaitingForDeviceUnlock or
            BackendRuntimeState.Stopping;
        var shellRequiresRestart = _stateSource.LastFailure?.RequiresProcessRestart == true;
        var requiresProcessRestart = owner.RequiresProcessRestart || shellRequiresRestart;
        var failure = owner.RequiresProcessRestart
            ? CreateFailure(
                IpcFailureKind.Runtime,
                "The Windows agent backend requires a clean process restart.",
                retryable: true,
                requiresRestart: true)
            : owner.State == WindowsAgentBackendOwnerState.Failed
                ? CreateFailure(
                    IpcFailureKind.Runtime,
                    "The Windows agent backend runtime failed.",
                    retryable: true,
                    requiresRestart: false)
                : _stateSource.LastFailure;
        var endpointReady = _stateSource.State == AgentState.Running &&
            _endpointHost.Snapshot.State == WindowsAgentEndpointHostState.Ready &&
            (owner.State is WindowsAgentBackendOwnerState.Ready or WindowsAgentBackendOwnerState.Interactive) &&
            !owner.IsResetting &&
            !requiresProcessRestart;

        return new AgentStatusDto(
            AgentState: _stateSource.State,
            IsUiConnected: _uiCoordinator.RegisteredConnectionId is not null,
            BackendOwnedByAgent: true,
            IsBackendRunning: runtimeRunning,
            IsBackgroundSyncEnabled: backgroundSyncEnabled,
            RequiresProcessRestart: requiresProcessRestart,
            LastFailure: failure,
            StartedAtUtc: _stateSource.StartedAtUtc,
            IsEndpointHostReady: endpointReady,
            IsDatabaseResetInProgress: _resetCoordinator.IsResetting,
            HasInteractiveUiLease: (owner.ActiveReasons & BackendLifetimeReason.InteractiveUi) != 0,
            HasBackgroundSyncLease: (owner.ActiveReasons & BackendLifetimeReason.BackgroundSync) != 0);
    }

    public Task<BackendRuntimeStatusDto> GetBackendRuntimeStatusAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = _backendOwner.Snapshot;
        if (owner.RequiresProcessRestart)
        {
            var failure = CreateFailure(
                IpcFailureKind.Runtime,
                "The Windows backend cannot be recreated safely in this agent process.",
                retryable: true,
                requiresRestart: true);
            return Task.FromResult(new BackendRuntimeStatusDto(
                BackendRuntimeStatusState.Failed,
                BackendRuntimeFailureStatusKind.ShutdownFailure,
                failure,
                RequiresProcessRestart: true,
                ChangedAtUtc: owner.ChangedAtUtc));
        }

        var ownerFailed = owner.State == WindowsAgentBackendOwnerState.Failed;
        var state = ownerFailed
            ? BackendRuntimeStatusState.Failed
            : MapRuntimeState(owner.Runtime.State);
        var kind = MapRuntimeFailureKind(owner.Runtime.FailureKind);
        if (ownerFailed && kind == BackendRuntimeFailureStatusKind.None)
            kind = BackendRuntimeFailureStatusKind.StartupFailure;
        var failureDto = ownerFailed || owner.Runtime.Failure is not null
            ? CreateRuntimeFailure(kind, requiresRestart: false)
            : null;
        return Task.FromResult(new BackendRuntimeStatusDto(
            state,
            kind,
            failureDto,
            RequiresProcessRestart: false,
            ChangedAtUtc: owner.Runtime.ChangedAtUtc));
    }

    public Task<InteractiveSessionStatusDto> GetInteractiveSessionStatusAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = _backendOwner.Snapshot;
        var state = owner.InteractiveSession.State switch
        {
            InteractiveSessionLifecycleState.None => InteractiveSessionStatusState.None,
            InteractiveSessionLifecycleState.Opening => InteractiveSessionStatusState.Opening,
            InteractiveSessionLifecycleState.Active =>
                _endpointAdapter.IsClosing
                    ? InteractiveSessionStatusState.Closing
                    : InteractiveSessionStatusState.Active,
            InteractiveSessionLifecycleState.Closing => InteractiveSessionStatusState.Closing,
            InteractiveSessionLifecycleState.CleanupFailed => InteractiveSessionStatusState.CleanupFailed,
            _ => InteractiveSessionStatusState.None
        };
        var cleanupFailure = state == InteractiveSessionStatusState.CleanupFailed
            ? CreateFailure(
                IpcFailureKind.InteractiveCleanup,
                "The interactive backend session could not clean up safely.",
                retryable: false,
                requiresRestart: owner.RequiresProcessRestart)
            : null;

        return Task.FromResult(new InteractiveSessionStatusDto(
            state,
            AcceptsNewOperations: state == InteractiveSessionStatusState.Active &&
                _endpointAdapter.AcceptsNewOperations,
            ActiveOperationCount: _endpointAdapter.ActiveOperationCount,
            CleanupFailure: cleanupFailure,
            ChangedAtUtc: owner.InteractiveSession.ChangedAtUtc));
    }

    public Task<SynchronizationStatusDto> GetSynchronizationStatusAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sync = _backendOwner.Snapshot.Synchronization;
        var state = sync.State switch
        {
            SyncRuntimeState.Disabled => SynchronizationStatusState.Disabled,
            SyncRuntimeState.Starting => SynchronizationStatusState.Starting,
            SyncRuntimeState.Running => SynchronizationStatusState.Running,
            SyncRuntimeState.Stopping => SynchronizationStatusState.Stopping,
            SyncRuntimeState.Degraded => SynchronizationStatusState.Degraded,
            _ => SynchronizationStatusState.Unavailable
        };
        var failure = state == SynchronizationStatusState.Degraded
            ? CreateFailure(
                IpcFailureKind.Synchronization,
                "Background synchronization is degraded.",
                retryable: true,
                requiresRestart: false)
            : null;
        return Task.FromResult(new SynchronizationStatusDto(state, failure));
    }

    private BackendRuntimeStatusState MapRuntimeState(BackendRuntimeState state) => state switch
    {
        BackendRuntimeState.NotStarted => BackendRuntimeStatusState.NotStarted,
        BackendRuntimeState.Starting => BackendRuntimeStatusState.Starting,
        BackendRuntimeState.Ready => BackendRuntimeStatusState.Ready,
        BackendRuntimeState.WaitingForDeviceUnlock => BackendRuntimeStatusState.WaitingForDeviceUnlock,
        BackendRuntimeState.Failed => BackendRuntimeStatusState.Failed,
        BackendRuntimeState.Stopping => BackendRuntimeStatusState.Stopping,
        BackendRuntimeState.Stopped => BackendRuntimeStatusState.Stopped,
        _ => BackendRuntimeStatusState.Unavailable
    };

    private static BackendRuntimeFailureStatusKind MapRuntimeFailureKind(
        BackendRuntimeFailureKind kind) => kind switch
    {
        BackendRuntimeFailureKind.None => BackendRuntimeFailureStatusKind.None,
        BackendRuntimeFailureKind.DatabaseCompatibility => BackendRuntimeFailureStatusKind.DatabaseCompatibility,
        BackendRuntimeFailureKind.PlatformKeyUnavailable => BackendRuntimeFailureStatusKind.PlatformKeyUnavailable,
        BackendRuntimeFailureKind.StorageUnavailable => BackendRuntimeFailureStatusKind.StorageUnavailable,
        BackendRuntimeFailureKind.StartupFailure => BackendRuntimeFailureStatusKind.StartupFailure,
        BackendRuntimeFailureKind.InteractiveCleanupFailure => BackendRuntimeFailureStatusKind.InteractiveCleanupFailure,
        BackendRuntimeFailureKind.ShutdownFailure => BackendRuntimeFailureStatusKind.ShutdownFailure,
        _ => BackendRuntimeFailureStatusKind.StartupFailure
    };

    private static IpcFailureDto CreateRuntimeFailure(
        BackendRuntimeFailureStatusKind kind,
        bool requiresRestart) => kind switch
    {
        BackendRuntimeFailureStatusKind.PlatformKeyUnavailable => CreateFailure(
            IpcFailureKind.PlatformKey,
            "The Windows platform key is unavailable until the device is unlocked.",
            retryable: true,
            requiresRestart),
        BackendRuntimeFailureStatusKind.StorageUnavailable => CreateFailure(
            IpcFailureKind.Storage,
            "Backend storage is unavailable.",
            retryable: true,
            requiresRestart),
        BackendRuntimeFailureStatusKind.InteractiveCleanupFailure => CreateFailure(
            IpcFailureKind.InteractiveCleanup,
            "The interactive backend session could not clean up safely.",
            retryable: false,
            requiresRestart),
        _ => CreateFailure(
            IpcFailureKind.Runtime,
            kind == BackendRuntimeFailureStatusKind.DatabaseCompatibility
                ? "The local database version is not supported."
                : "The Windows backend runtime failed.",
            retryable: kind != BackendRuntimeFailureStatusKind.DatabaseCompatibility,
            requiresRestart)
    };

    private static IpcFailureDto CreateFailure(
        IpcFailureKind kind,
        string message,
        bool retryable,
        bool requiresRestart) => new(
            kind,
            message,
            DateTimeOffset.UtcNow,
            retryable,
            requiresRestart);
}
