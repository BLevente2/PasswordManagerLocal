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
    private readonly DateTimeOffset _statusEpochUtc;

    public WindowsAgentStatusProvider(
        IWindowsAgentStateSource stateSource,
        IUiConnectionCoordinator uiCoordinator,
        IWindowsBackgroundSyncSettingsReader settingsReader)
    {
        _stateSource = stateSource ?? throw new ArgumentNullException(nameof(stateSource));
        _uiCoordinator = uiCoordinator ?? throw new ArgumentNullException(nameof(uiCoordinator));
        _settingsReader = settingsReader ?? throw new ArgumentNullException(nameof(settingsReader));
        _statusEpochUtc = DateTimeOffset.UtcNow;
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

        return new AgentStatusDto(
            AgentState: _stateSource.State,
            IsUiConnected: _uiCoordinator.RegisteredConnectionId is not null,
            BackendOwnedByAgent: false,
            IsBackendRunning: false,
            IsBackgroundSyncEnabled: backgroundSyncEnabled,
            RequiresProcessRestart: false,
            LastFailure: _stateSource.LastFailure,
            StartedAtUtc: _stateSource.StartedAtUtc);
    }

    public Task<BackendRuntimeStatusDto> GetBackendRuntimeStatusAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new BackendRuntimeStatusDto(
            RuntimeState: BackendRuntimeStatusState.Unavailable,
            FailureKind: BackendRuntimeFailureStatusKind.None,
            Failure: null,
            RequiresProcessRestart: false,
            ChangedAtUtc: _statusEpochUtc));
    }

    public Task<InteractiveSessionStatusDto> GetInteractiveSessionStatusAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new InteractiveSessionStatusDto(
            LifecycleState: InteractiveSessionStatusState.None,
            AcceptsNewOperations: false,
            ActiveOperationCount: 0,
            CleanupFailure: null,
            ChangedAtUtc: _statusEpochUtc));
    }

    public Task<SynchronizationStatusDto> GetSynchronizationStatusAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new SynchronizationStatusDto(
            State: SynchronizationStatusState.Unavailable,
            LastFailure: null));
    }
}
