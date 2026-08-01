using PasswordManagerLocal.Common.Backend.Hosting;
using PasswordManagerLocal.Common.Contracts.Runtime;
using PasswordManagerLocal.Common.Contracts.BackgroundSync;
using PasswordManagerLocal.Windows.Agent.Backend;
using PasswordManagerLocal.Windows.Agent.Background;
using PasswordManagerLocal.Windows.Agent.Lifecycle;
using PasswordManagerLocal.Windows.Agent.Endpoint;
using PasswordManagerLocal.Windows.Agent.Hosting;
using PasswordManagerLocal.Windows.Ipc.Contracts;

namespace PasswordManagerLocal.Windows.Agent.DatabaseReset;

public sealed class WindowsAgentDatabaseResetCoordinator : IWindowsAgentDatabaseResetCoordinator
{
    private readonly IWindowsAgentEndpointHost _endpointHost;
    private readonly IWindowsAgentBackendRuntimeOwner _backendOwner;
    private readonly WindowsAgentShutdownCoordinator _shutdownCoordinator;
    private readonly IWindowsBackgroundSyncCoordinator _backgroundSyncCoordinator;
    private readonly WindowsAgentLifecycleTransitionCoordinator _lifecycleTransitions;
    private readonly SemaphoreSlim _resetGate = new(1, 1);
    private int _resetting;

    public WindowsAgentDatabaseResetCoordinator(
        IWindowsAgentEndpointHost endpointHost,
        IWindowsAgentBackendRuntimeOwner backendOwner,
        WindowsAgentShutdownCoordinator shutdownCoordinator,
        IWindowsBackgroundSyncCoordinator backgroundSyncCoordinator,
        WindowsAgentLifecycleTransitionCoordinator lifecycleTransitions)
    {
        _endpointHost = endpointHost ?? throw new ArgumentNullException(nameof(endpointHost));
        _backendOwner = backendOwner ?? throw new ArgumentNullException(nameof(backendOwner));
        _shutdownCoordinator = shutdownCoordinator ?? throw new ArgumentNullException(nameof(shutdownCoordinator));
        _backgroundSyncCoordinator = backgroundSyncCoordinator
            ?? throw new ArgumentNullException(nameof(backgroundSyncCoordinator));
        _lifecycleTransitions = lifecycleTransitions
            ?? throw new ArgumentNullException(nameof(lifecycleTransitions));
    }

    public bool IsResetting => Volatile.Read(ref _resetting) != 0;

    public async Task<DatabaseResetResultDto> ResetAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await _resetGate.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            return new DatabaseResetResultDto(
                Completed: false,
                RequiresProcessRestart: false,
                SafeMessage: "A database reset is already in progress.");
        }

        WindowsAgentLifecycleTransitionLease? transition = null;
        Interlocked.Exchange(ref _resetting, 1);
        try
        {
            transition = await _lifecycleTransitions.EnterAsync(
                WindowsAgentLifecycleTransitionState.ResettingDatabase,
                cancellationToken);
            var snapshot = _backendOwner.Snapshot;
            if (snapshot.RequiresProcessRestart || snapshot.IsResetting ||
                snapshot.Runtime.State != BackendRuntimeState.Failed ||
                snapshot.Runtime.FailureKind != BackendRuntimeFailureKind.DatabaseCompatibility)
            {
                return new DatabaseResetResultDto(
                    Completed: false,
                    RequiresProcessRestart: snapshot.RequiresProcessRestart,
                    SafeMessage: snapshot.RequiresProcessRestart
                        ? "The Windows agent must restart before the database can be reset."
                        : "The database reset is only available after a database compatibility failure.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            // Once endpoint admission closes, reset completion is independent of the requesting UI connection.
            var backgroundEnabled = await _backgroundSyncCoordinator.SuspendForDatabaseResetAsync(
                CancellationToken.None);
            await _endpointHost.StopAsync(CancellationToken.None);
            await _backendOwner.ResetDatabaseAsync(CancellationToken.None);
            await _backgroundSyncCoordinator.RestoreAfterDatabaseResetAsync(
                backgroundEnabled,
                CancellationToken.None);
            await _endpointHost.StartAsync(CancellationToken.None);
            return new DatabaseResetResultDto(
                Completed: true,
                RequiresProcessRestart: false,
                SafeMessage: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _backendOwner.RequireProcessRestart(exception);
            _shutdownCoordinator.RequestShutdown(WindowsAgentShutdownReason.RestartRequired);
            return new DatabaseResetResultDto(
                Completed: false,
                RequiresProcessRestart: true,
                SafeMessage: "The database reset could not complete safely. The agent must restart.");
        }
        finally
        {
            if (transition is not null)
                await transition.DisposeAsync();
            Interlocked.Exchange(ref _resetting, 0);
            _resetGate.Release();
        }
    }
}
