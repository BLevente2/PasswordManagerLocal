using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Runtime.Abstractions;
using PasswordManagerLocal.Windows.EndpointRpc.Serialization;
using PasswordManagerLocal.Windows.EndpointRpc.Validation;

namespace PasswordManagerLocal.Windows.EndpointRpc.Client;

public sealed class WindowsNamedPipeFrontendBackendClient : IFrontendBackendClient<IEndpoints>
{
    private readonly IEndpointRpcClientConnector _connector;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly object _snapshotGate = new();
    private IEndpointRpcTransport? _transport;
    private NamedPipeEndpointsProxy? _proxy;
    private Task? _connectionObserverTask;
    private BackendRuntimeSnapshot _snapshot = new(
        BackendRuntimeState.NotStarted,
        BackendRuntimeFailureKind.None,
        null,
        DateTimeOffset.UtcNow);
    private bool _disposed;

    public WindowsNamedPipeFrontendBackendClient(
        string pipeName,
        int processId,
        Guid sessionId)
        : this(new WindowsNamedPipeEndpointRpcConnector(pipeName, processId, sessionId))
    {
    }

    public WindowsNamedPipeFrontendBackendClient(IEndpointRpcClientConnector connector) =>
        _connector = connector ?? throw new ArgumentNullException(nameof(connector));

    public BackendRuntimeSnapshot Snapshot
    {
        get
        {
            ThrowIfDisposed();
            lock (_snapshotGate)
                return _snapshot;
        }
    }

    public event EventHandler<BackendRuntimeStateChangedEventArgs>? StateChanged;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_transport?.IsConnected == true && _proxy is not null)
                return;

            var staleTransport = _transport;
            var staleObserverTask = _connectionObserverTask;
            _transport = null;
            _proxy = null;
            _connectionObserverTask = null;

            ChangeState(BackendRuntimeState.Starting, BackendRuntimeFailureKind.None, null);
            IEndpointRpcTransport? transport = null;
            try
            {
                if (staleTransport is not null)
                    await staleTransport.DisposeAsync();
                if (staleObserverTask is not null)
                    await staleObserverTask;

                transport = await _connector.ConnectAsync(cancellationToken);
                if (!transport.IsConnected)
                    throw new EndpointRpcDisconnectedException();

                _transport = transport;
                _proxy = new NamedPipeEndpointsProxy(
                    transport,
                    new EndpointRpcSerializer(),
                    new EndpointRpcContractValidator());
                _connectionObserverTask = ObserveConnectionAsync(transport);
                ChangeState(BackendRuntimeState.Ready, BackendRuntimeFailureKind.None, null);
            }
            catch (Exception exception)
            {
                Exception failure = exception;
                if (transport is not null)
                {
                    try
                    {
                        await transport.DisposeAsync();
                    }
                    catch (Exception disposalException)
                    {
                        failure = new AggregateException(exception, disposalException);
                    }
                }

                ChangeState(
                    BackendRuntimeState.Failed,
                    BackendRuntimeFailureKind.StartupFailure,
                    failure);
                if (ReferenceEquals(failure, exception))
                    throw;
                throw failure;
            }
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

    public Task ResetDatabaseAndRestartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException(
            "Database reset through the endpoint channel is not available before the production cutover.");
    }

    public async Task<IEndpoints> GetEndpointsAsync(
        CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (_transport?.IsConnected != true || _proxy is null)
                throw new InvalidOperationException("The endpoint backend client is not connected.");
            return _proxy;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleLock.WaitAsync(CancellationToken.None);
        Task? observerTask;
        Exception? failure = null;
        try
        {
            if (_disposed)
                return;

            _disposed = true;
            ChangeState(BackendRuntimeState.Stopping, BackendRuntimeFailureKind.None, null);
            var transport = _transport;
            _transport = null;
            _proxy = null;
            observerTask = _connectionObserverTask;
            _connectionObserverTask = null;

            if (transport is not null)
            {
                try
                {
                    await transport.DisposeAsync();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }

        if (observerTask is not null)
        {
            try
            {
                await observerTask;
            }
            catch (Exception exception)
            {
                failure = failure is null ? exception : new AggregateException(failure, exception);
            }
        }

        ChangeState(
            BackendRuntimeState.Stopped,
            failure is null ? BackendRuntimeFailureKind.None : BackendRuntimeFailureKind.ShutdownFailure,
            failure);
        StateChanged = null;
        GC.SuppressFinalize(this);

        if (failure is not null)
            throw failure;
    }

    private async Task ObserveConnectionAsync(IEndpointRpcTransport transport)
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

        if (_disposed || !ReferenceEquals(_transport, transport))
            return;

        ChangeState(
            BackendRuntimeState.Failed,
            BackendRuntimeFailureKind.StorageUnavailable,
            failure ?? new EndpointRpcDisconnectedException());
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
            try
            {
                handler(this, args);
            }
            catch
            {
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WindowsNamedPipeFrontendBackendClient));
    }
}
