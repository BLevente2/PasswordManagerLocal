using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Backend.Constants;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Runtime.Abstractions;
using PasswordManagerLocal.Windows.EndpointRpc.Serialization;
using PasswordManagerLocal.Windows.EndpointRpc.Validation;
using PasswordManagerLocal.Windows.Ipc.Contracts;

namespace PasswordManagerLocal.Windows.EndpointRpc.Client;

public sealed class WindowsNamedPipeFrontendBackendClient : IFrontendBackendClient<IEndpoints>
{
    private readonly IEndpointRpcClientConnector _connector;
    private readonly IEndpointRpcAgentConnection? _agentConnection;
    private readonly int _maximumRecoveryAttempts;
    private readonly TimeSpan _recoveryDelay;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly CancellationTokenSource _lifetimeSource = new();
    private readonly object _snapshotGate = new();
    private readonly object _taskGate = new();
    private readonly List<Task> _observerTasks = [];
    private IEndpointRpcTransport? _transport;
    private NamedPipeEndpointsProxy? _proxy;
    private Task _recoveryTask = Task.CompletedTask;
    private long _observedAgentGeneration;
    private WindowsEndpointClientConnectionState _connectionState = WindowsEndpointClientConnectionState.Disconnected;
    private BackendRuntimeSnapshot _snapshot = new(
        BackendRuntimeState.NotStarted,
        BackendRuntimeFailureKind.None,
        null,
        DateTimeOffset.UtcNow);
    private bool _disposed;
    private int _disposeStarted;
    private int _recoverySuppressed;

    public WindowsNamedPipeFrontendBackendClient(
        string pipeName,
        WindowsUiIpcIdentity identity)
        : this(new WindowsNamedPipeEndpointRpcConnector(pipeName, identity))
    {
    }

    public WindowsNamedPipeFrontendBackendClient(IEndpointRpcClientConnector connector)
        : this(null, connector)
    {
    }

    public WindowsNamedPipeFrontendBackendClient(
        IEndpointRpcAgentConnection? agentConnection,
        IEndpointRpcClientConnector connector,
        int maximumRecoveryAttempts = 3,
        TimeSpan? recoveryDelay = null)
    {
        _agentConnection = agentConnection;
        _connector = connector ?? throw new ArgumentNullException(nameof(connector));
        if (maximumRecoveryAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumRecoveryAttempts));
        _maximumRecoveryAttempts = maximumRecoveryAttempts;
        _recoveryDelay = recoveryDelay ?? TimeSpan.FromMilliseconds(350);
        if (_recoveryDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(recoveryDelay));
    }

    public BackendRuntimeSnapshot Snapshot
    {
        get
        {
            ThrowIfDisposed();
            lock (_snapshotGate)
                return _snapshot;
        }
    }

    public WindowsEndpointClientConnectionState ConnectionState
    {
        get
        {
            lock (_snapshotGate)
                return _connectionState;
        }
    }

    public event EventHandler<BackendRuntimeStateChangedEventArgs>? StateChanged;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_transport?.IsConnected == true && _proxy is not null)
            {
                ChangeConnectionState(WindowsEndpointClientConnectionState.Ready);
                return;
            }

            ChangeConnectionState(WindowsEndpointClientConnectionState.Connecting);
            if (_agentConnection is not null &&
                !await _agentConnection.EnsureConnectedAsync(cancellationToken))
            {
                throw new EndpointRpcDisconnectedException(
                    new InvalidOperationException("The Windows agent control connection is unavailable."));
            }

            ChangeState(BackendRuntimeState.Starting, BackendRuntimeFailureKind.None, null);
            await DisposeEndpointConnectionLockedAsync();
            await ConnectEndpointLockedAsync(cancellationToken);
            StartAgentObserverLocked();
            ChangeState(BackendRuntimeState.Ready, BackendRuntimeFailureKind.None, null);
            ChangeConnectionState(WindowsEndpointClientConnectionState.Ready);
        }
        catch (Exception exception)
        {
            var mapped = await MapAgentRuntimeFailureAsync(exception, cancellationToken);
            ChangeState(BackendRuntimeState.Failed, mapped.FailureKind, mapped.Exception);
            ChangeConnectionState(WindowsEndpointClientConnectionState.Unavailable);
            if (!ReferenceEquals(mapped.Exception, exception))
                throw mapped.Exception;
            throw;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public Task WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (Snapshot.State != BackendRuntimeState.Ready || _transport?.IsConnected != true)
            throw new InvalidOperationException("The endpoint backend client is not ready.");
        return Task.CompletedTask;
    }

    public async Task ResetDatabaseAndRestartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_agentConnection is null)
        {
            throw new NotSupportedException(
                "Database reset requires the agent control connection.");
        }

        var requiresProcessRestart = false;
        Interlocked.Exchange(ref _recoverySuppressed, 1);
        try
        {
            await _lifecycleLock.WaitAsync(cancellationToken);
            try
            {
                ThrowIfDisposed();
                ChangeState(BackendRuntimeState.Stopping, BackendRuntimeFailureKind.None, null);
                ChangeConnectionState(WindowsEndpointClientConnectionState.Unavailable);
                await DisposeEndpointConnectionLockedAsync();
            }
            finally
            {
                _lifecycleLock.Release();
            }

            var result = await _agentConnection.ResetDatabaseAsync(cancellationToken);
            requiresProcessRestart = result.RequiresProcessRestart;
            if (!result.Completed)
            {
                var failure = new InvalidOperationException(
                    result.SafeMessage ?? "The database reset did not complete.");
                ChangeState(
                    BackendRuntimeState.Failed,
                    requiresProcessRestart
                        ? BackendRuntimeFailureKind.ShutdownFailure
                        : BackendRuntimeFailureKind.StorageUnavailable,
                    failure);
                throw failure;
            }

            await ConnectAsync(cancellationToken);
        }
        finally
        {
            Interlocked.Exchange(ref _recoverySuppressed, 0);
            if (Volatile.Read(ref _disposeStarted) == 0 &&
                _transport?.IsConnected != true)
            {
                StartRecovery();
            }
        }
    }

    public async Task<IEndpoints> GetEndpointsAsync(
        CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (_transport?.IsConnected != true || _proxy is null ||
                Snapshot.State != BackendRuntimeState.Ready)
            {
                throw new InvalidOperationException("The endpoint backend client is unavailable.");
            }
            return _proxy;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return;

        ChangeConnectionState(WindowsEndpointClientConnectionState.Disposed);
        _lifetimeSource.Cancel();
        Interlocked.Exchange(ref _recoverySuppressed, 1);
        Exception? failure = null;
        await _lifecycleLock.WaitAsync(CancellationToken.None);
        try
        {
            _disposed = true;
            ChangeState(BackendRuntimeState.Stopping, BackendRuntimeFailureKind.None, null);
            try
            {
                await DisposeEndpointConnectionLockedAsync();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }

        if (_agentConnection is not null)
        {
            try
            {
                await _agentConnection.DisposeAsync();
            }
            catch (Exception exception)
            {
                failure = Combine(failure, exception);
            }
        }

        Task[] backgroundTasks;
        lock (_taskGate)
            backgroundTasks = [.. _observerTasks, _recoveryTask];
        try
        {
            await Task.WhenAll(backgroundTasks);
        }
        catch (Exception exception)
        {
            failure = Combine(failure, exception);
        }

        ChangeState(
            BackendRuntimeState.Stopped,
            failure is null ? BackendRuntimeFailureKind.None : BackendRuntimeFailureKind.ShutdownFailure,
            failure);
        StateChanged = null;
        _lifetimeSource.Dispose();
        _lifecycleLock.Dispose();
        GC.SuppressFinalize(this);

        if (failure is not null)
            throw failure;
    }

    private async Task ConnectEndpointLockedAsync(CancellationToken cancellationToken)
    {
        var transport = await _connector.ConnectAsync(cancellationToken);
        if (!transport.IsConnected)
        {
            await transport.DisposeAsync();
            throw new EndpointRpcDisconnectedException();
        }

        _transport = transport;
        _proxy = new NamedPipeEndpointsProxy(
            transport,
            new EndpointRpcSerializer(),
            new EndpointRpcContractValidator());
        TrackBackgroundTask(ObserveEndpointConnectionAsync(transport));
    }

    private void StartAgentObserverLocked()
    {
        if (_agentConnection is null || !_agentConnection.IsConnected)
            return;

        var generation = _agentConnection.ConnectionGeneration;
        if (_observedAgentGeneration == generation)
            return;

        _observedAgentGeneration = generation;
        TrackBackgroundTask(ObserveAgentConnectionAsync(generation, _agentConnection.Completion));
    }

    private async Task ObserveEndpointConnectionAsync(IEndpointRpcTransport transport)
    {
        Exception? failure = null;
        try
        {
            await transport.Completion;
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        await HandleObservedConnectionLossAsync(
            transport,
            failure ?? new EndpointRpcDisconnectedException());
    }

    private async Task ObserveAgentConnectionAsync(long generation, Task completion)
    {
        Exception? failure = null;
        try
        {
            await completion;
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (_agentConnection is null ||
            _agentConnection.ConnectionGeneration != generation)
        {
            return;
        }

        await HandleObservedConnectionLossAsync(
            expectedTransport: null,
            failure ?? new EndpointRpcDisconnectedException(
                new InvalidOperationException("The Windows agent control connection was lost.")));
    }

    private async Task HandleObservedConnectionLossAsync(
        IEndpointRpcTransport? expectedTransport,
        Exception failure)
    {
        try
        {
            await HandleConnectionLossAsync(expectedTransport, failure);
        }
        catch (Exception cleanupFailure)
        {
            if (_disposed || Volatile.Read(ref _disposeStarted) != 0)
                return;

            ChangeState(
                BackendRuntimeState.Failed,
                BackendRuntimeFailureKind.StorageUnavailable,
                Combine(failure, cleanupFailure));
            ChangeConnectionState(WindowsEndpointClientConnectionState.Unavailable);
            StartRecovery();
        }
    }

    private async Task HandleConnectionLossAsync(
        IEndpointRpcTransport? expectedTransport,
        Exception failure)
    {
        if (_disposed || Volatile.Read(ref _recoverySuppressed) != 0)
            return;

        await _lifecycleLock.WaitAsync(CancellationToken.None);
        try
        {
            if (_disposed || Volatile.Read(ref _recoverySuppressed) != 0)
                return;
            if (expectedTransport is not null && !ReferenceEquals(_transport, expectedTransport))
                return;

            ChangeState(
                BackendRuntimeState.Failed,
                BackendRuntimeFailureKind.StorageUnavailable,
                failure);
            ChangeConnectionState(WindowsEndpointClientConnectionState.Unavailable);
            await DisposeEndpointConnectionLockedAsync();
        }
        finally
        {
            _lifecycleLock.Release();
        }

        StartRecovery();
    }

    private void StartRecovery()
    {
        lock (_taskGate)
        {
            if (!_recoveryTask.IsCompleted)
                return;
            _recoveryTask = RecoverAsync();
        }
    }

    private async Task RecoverAsync()
    {
        if (_disposed || Volatile.Read(ref _recoverySuppressed) != 0 ||
            _agentConnection is null)
        {
            return;
        }

        ChangeConnectionState(WindowsEndpointClientConnectionState.Reconnecting);
        for (var attempt = 0; attempt < _maximumRecoveryAttempts; attempt++)
        {
            try
            {
                if (attempt > 0)
                    await Task.Delay(_recoveryDelay, _lifetimeSource.Token);

                if (!await _agentConnection.EnsureConnectedAsync(_lifetimeSource.Token))
                    continue;

                await _lifecycleLock.WaitAsync(_lifetimeSource.Token);
                try
                {
                    if (_disposed || Volatile.Read(ref _recoverySuppressed) != 0)
                        return;

                    ChangeState(BackendRuntimeState.Starting, BackendRuntimeFailureKind.None, null);
                    await DisposeEndpointConnectionLockedAsync();
                    await ConnectEndpointLockedAsync(_lifetimeSource.Token);
                    StartAgentObserverLocked();
                    ChangeState(BackendRuntimeState.Ready, BackendRuntimeFailureKind.None, null);
                    ChangeConnectionState(WindowsEndpointClientConnectionState.Ready);
                    return;
                }
                finally
                {
                    _lifecycleLock.Release();
                }
            }
            catch (OperationCanceledException) when (_lifetimeSource.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                if (_lifetimeSource.IsCancellationRequested)
                    return;

                (Exception Exception, BackendRuntimeFailureKind FailureKind) mapped;
                try
                {
                    mapped = await MapAgentRuntimeFailureAsync(exception, _lifetimeSource.Token);
                }
                catch (OperationCanceledException) when (_lifetimeSource.IsCancellationRequested)
                {
                    return;
                }

                ChangeState(
                    BackendRuntimeState.Failed,
                    mapped.FailureKind == BackendRuntimeFailureKind.StartupFailure
                        ? BackendRuntimeFailureKind.StorageUnavailable
                        : mapped.FailureKind,
                    mapped.Exception);
                if (mapped.FailureKind == BackendRuntimeFailureKind.ShutdownFailure)
                    return;
            }
        }

        ChangeConnectionState(WindowsEndpointClientConnectionState.Unavailable);
    }

    private async Task DisposeEndpointConnectionLockedAsync()
    {
        var transport = _transport;
        var proxy = _proxy;
        _transport = null;
        _proxy = null;

        Exception? failure = null;
        if (transport is not null)
        {
            try { await transport.DisposeAsync(); }
            catch (Exception exception) { failure = exception; }
        }
        if (proxy is not null)
        {
            try { await proxy.DisposeAsync(); }
            catch (Exception exception) { failure = Combine(failure, exception); }
        }
        if (failure is not null)
            throw failure;
    }

    private async Task<(Exception Exception, BackendRuntimeFailureKind FailureKind)> MapAgentRuntimeFailureAsync(
        Exception original,
        CancellationToken cancellationToken)
    {
        if (_agentConnection is null || !_agentConnection.IsConnected)
            return (original, BackendRuntimeFailureKind.StartupFailure);

        try
        {
            var status = await _agentConnection.GetBackendRuntimeStatusAsync(cancellationToken);
            if (status.RequiresProcessRestart)
            {
                return (
                    new InvalidOperationException(
                        "The Windows agent must restart before the backend can be used.",
                        original),
                    BackendRuntimeFailureKind.ShutdownFailure);
            }

            return status.FailureKind switch
            {
                BackendRuntimeFailureStatusKind.DatabaseCompatibility =>
                    (new DatabaseVersionNotSupportedException(
                        detectedVersion: null,
                        oldestSupportedVersion: DatabaseConstants.OldestSupportedDbVersion,
                        currentVersion: DatabaseConstants.CurrentDbVersion,
                        innerException: original),
                     BackendRuntimeFailureKind.DatabaseCompatibility),
                BackendRuntimeFailureStatusKind.PlatformKeyUnavailable =>
                    (new KeyProtectorUnavailableException(
                        PasswordManagerLocal.Backend.Models.KeyProtectorUnavailableReason.PlatformKeyStoreUnavailable,
                        original),
                     BackendRuntimeFailureKind.PlatformKeyUnavailable),
                BackendRuntimeFailureStatusKind.StorageUnavailable =>
                    (original, BackendRuntimeFailureKind.StorageUnavailable),
                BackendRuntimeFailureStatusKind.InteractiveCleanupFailure =>
                    (original, BackendRuntimeFailureKind.InteractiveCleanupFailure),
                BackendRuntimeFailureStatusKind.ShutdownFailure =>
                    (original, BackendRuntimeFailureKind.ShutdownFailure),
                _ => (original, BackendRuntimeFailureKind.StartupFailure)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return (original, BackendRuntimeFailureKind.StartupFailure);
        }
    }

    private void TrackBackgroundTask(Task task)
    {
        lock (_taskGate)
            _observerTasks.Add(task);
    }

    private void ChangeState(
        BackendRuntimeState state,
        BackendRuntimeFailureKind failureKind,
        Exception? failure)
    {
        BackendRuntimeSnapshot previous;
        BackendRuntimeSnapshot current;
        lock (_snapshotGate)
        {
            previous = _snapshot;
            current = new BackendRuntimeSnapshot(
                state,
                failureKind,
                failure,
                DateTimeOffset.UtcNow);
            _snapshot = current;
        }

        if (previous == current)
            return;

        var handlers = StateChanged;
        if (handlers is null)
            return;

        var args = new BackendRuntimeStateChangedEventArgs(previous, current);
        foreach (EventHandler<BackendRuntimeStateChangedEventArgs> handler in handlers.GetInvocationList())
        {
            try { handler(this, args); } catch { }
        }
    }

    private void ChangeConnectionState(WindowsEndpointClientConnectionState state)
    {
        lock (_snapshotGate)
            _connectionState = state;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed || Volatile.Read(ref _disposeStarted) != 0)
            throw new ObjectDisposedException(nameof(WindowsNamedPipeFrontendBackendClient));
    }

    private Exception Combine(Exception? first, Exception second) =>
        first is null ? second : new AggregateException(first, second);
}
