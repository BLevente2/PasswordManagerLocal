using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Backend.Hosting;
using PasswordManagerLocal.Runtime.Abstractions;

namespace PasswordManagerLocal.Android.Runtime;

public sealed class AndroidServiceFrontendBackendClient : IFrontendBackendClient<IEndpoints>
{
    private readonly AndroidRuntimeServiceHost _owner;
    private readonly IBackendRuntime _runtime;
    private readonly IBackendRuntimeLifetimeCoordinator _lifetimeCoordinator;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private IBackendRuntimeLease? _interactiveLease;
    private IInteractiveBackendSession? _interactiveSession;
    private bool _disposed;

    internal AndroidServiceFrontendBackendClient(
        AndroidRuntimeServiceHost owner,
        IBackendRuntime runtime,
        IBackendRuntimeLifetimeCoordinator lifetimeCoordinator)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _lifetimeCoordinator = lifetimeCoordinator
            ?? throw new ArgumentNullException(nameof(lifetimeCoordinator));
        _runtime.StateChanged += HandleRuntimeStateChanged;
    }

    public BackendRuntimeSnapshot Snapshot
    {
        get
        {
            ThrowIfDisposed();
            return _runtime.Snapshot;
        }
    }

    public event EventHandler<BackendRuntimeStateChangedEventArgs>? StateChanged;

    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        _owner.EnsureInteractiveConnectionAsync(this, cancellationToken);

    public async Task WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_interactiveSession is null)
                throw new InvalidOperationException("The Android interactive service attachment is not connected.");

            await _runtime.WaitUntilReadyAsync(cancellationToken);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public Task ResetDatabaseAndRestartAsync(CancellationToken cancellationToken = default) =>
        _owner.ResetDatabaseAsync(this, cancellationToken);

    public async Task<IEndpoints> GetEndpointsAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            return _interactiveSession?.Endpoints
                ?? throw new InvalidOperationException("The Android interactive service attachment is not connected.");
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await _owner.DetachInteractiveClientAsync(this);
    }

    internal async Task OpenFromHostAsync(CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_interactiveSession is not null)
                return;

            var lease = await _lifetimeCoordinator.AcquireAsync(
                BackendLifetimeReason.InteractiveUi,
                cancellationToken);
            try
            {
                var session = await _runtime.OpenInteractiveSessionAsync(cancellationToken);
                _interactiveLease = lease;
                _interactiveSession = session;
            }
            catch (Exception connectionException)
            {
                Exception failure = connectionException;
                if (_runtime.InteractiveSessionSnapshot.RequiresRecovery)
                {
                    try
                    {
                        await _lifetimeCoordinator.RecoverRuntimeAsync(CancellationToken.None);
                    }
                    catch (Exception recoveryException)
                    {
                        failure = new AggregateException(failure, recoveryException);
                    }
                }

                try
                {
                    await lease.DisposeAsync();
                }
                catch (Exception cleanupException)
                {
                    failure = new AggregateException(failure, cleanupException);
                }

                throw failure;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    internal async Task CloseFromHostAsync()
    {
        await _lifecycleLock.WaitAsync(CancellationToken.None);
        Exception? failure = null;
        try
        {
            var session = _interactiveSession;
            var lease = _interactiveLease;
            _interactiveSession = null;
            _interactiveLease = null;

            if (session is not null)
            {
                try
                {
                    await session.DisposeAsync();
                }
                catch (Exception exception)
                {
                    failure = exception;
                    if (_runtime.InteractiveSessionSnapshot.RequiresRecovery)
                    {
                        try
                        {
                            await _lifetimeCoordinator.RecoverRuntimeAsync(CancellationToken.None);
                        }
                        catch (Exception recoveryException)
                        {
                            failure = new AggregateException(failure, recoveryException);
                        }
                    }
                }
            }

            if (lease is not null)
            {
                try
                {
                    await lease.DisposeAsync();
                }
                catch (Exception exception)
                {
                    failure = failure is null
                        ? exception
                        : new AggregateException(failure, exception);
                }
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }

        if (failure is not null)
            throw failure;
    }

    internal async Task AdoptResetConnectionFromHostAsync(
        IBackendRuntimeLease lease,
        IInteractiveBackendSession session)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(session);

        await _lifecycleLock.WaitAsync(CancellationToken.None);
        try
        {
            ThrowIfDisposed();
            if (_interactiveLease is not null || _interactiveSession is not null)
                throw new InvalidOperationException("The Android interactive connection is already active.");

            _interactiveLease = lease;
            _interactiveSession = session;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    internal void CompleteDisposalFromHost()
    {
        if (_disposed)
            return;

        _disposed = true;
        _runtime.StateChanged -= HandleRuntimeStateChanged;
        StateChanged = null;
        GC.SuppressFinalize(this);
    }

    private void HandleRuntimeStateChanged(
        object? sender,
        BackendRuntimeStateChangedEventArgs args)
    {
        if (_disposed)
            return;

        var handlers = StateChanged;
        if (handlers is null)
            return;

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
            throw new ObjectDisposedException(nameof(AndroidServiceFrontendBackendClient));
    }
}
