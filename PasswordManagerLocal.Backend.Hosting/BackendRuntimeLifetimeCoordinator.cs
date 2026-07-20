using PasswordManagerLocal.Runtime.Abstractions;

namespace PasswordManagerLocal.Backend.Hosting;

public sealed class BackendRuntimeLifetimeCoordinator : IBackendRuntimeLifetimeCoordinator
{
    private readonly IBackendRuntime _runtime;
    private readonly SemaphoreSlim _transitionLock = new(1, 1);
    private readonly object _stateLock = new();
    private int _interactiveUiCount;
    private int _backgroundSyncCount;

    public BackendRuntimeLifetimeCoordinator(IBackendRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public BackendLifetimeReason ActiveReasons
    {
        get
        {
            lock (_stateLock)
            {
                var reasons = BackendLifetimeReason.None;
                if (_interactiveUiCount > 0)
                    reasons |= BackendLifetimeReason.InteractiveUi;
                if (_backgroundSyncCount > 0)
                    reasons |= BackendLifetimeReason.BackgroundSync;
                return reasons;
            }
        }
    }

    public async Task<IBackendRuntimeLease> AcquireAsync(
        BackendLifetimeReason reason,
        CancellationToken cancellationToken = default)
    {
        ValidateReason(reason);
        await _transitionLock.WaitAsync(cancellationToken);
        var shouldStopAfterCancellation = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (GetTotalLeaseCount() == 0)
            {
                shouldStopAfterCancellation = true;
                await _runtime.EnsureStartedAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }

            Increment(reason);
            shouldStopAfterCancellation = false;
            return new BackendRuntimeLease(this, reason);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (shouldStopAfterCancellation && GetTotalLeaseCount() == 0)
                await _runtime.StopAsync(CancellationToken.None);

            throw;
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    public async Task<IBackendRuntimeLease> ResetDatabaseAndAcquireAsync(
        BackendLifetimeReason reason,
        CancellationToken cancellationToken = default)
    {
        ValidateReason(reason);
        await _transitionLock.WaitAsync(cancellationToken);
        var shouldStopAfterCancellation = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GetTotalLeaseCount() != 0)
            {
                throw new InvalidOperationException(
                    "The database cannot be reset while runtime leases are active.");
            }

            shouldStopAfterCancellation = true;
            await _runtime.ResetDatabaseAndRestartAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            Increment(reason);
            shouldStopAfterCancellation = false;
            return new BackendRuntimeLease(this, reason);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (shouldStopAfterCancellation && GetTotalLeaseCount() == 0)
                await _runtime.StopAsync(CancellationToken.None);

            throw;
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    internal async ValueTask ReleaseAsync(BackendLifetimeReason reason)
    {
        await _transitionLock.WaitAsync(CancellationToken.None);
        try
        {
            Decrement(reason);
            if (GetTotalLeaseCount() == 0)
                await _runtime.StopAsync(CancellationToken.None);
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    private int GetTotalLeaseCount()
    {
        lock (_stateLock)
            return _interactiveUiCount + _backgroundSyncCount;
    }

    private void Increment(BackendLifetimeReason reason)
    {
        lock (_stateLock)
        {
            if (reason == BackendLifetimeReason.InteractiveUi)
                _interactiveUiCount++;
            else
                _backgroundSyncCount++;
        }
    }

    private void Decrement(BackendLifetimeReason reason)
    {
        lock (_stateLock)
        {
            if (reason == BackendLifetimeReason.InteractiveUi)
            {
                if (_interactiveUiCount == 0)
                    throw new InvalidOperationException("No interactive UI runtime lease is active.");

                _interactiveUiCount--;
            }
            else
            {
                if (_backgroundSyncCount == 0)
                    throw new InvalidOperationException("No background synchronization runtime lease is active.");

                _backgroundSyncCount--;
            }
        }
    }

    private static void ValidateReason(BackendLifetimeReason reason)
    {
        if (reason is not BackendLifetimeReason.InteractiveUi and not BackendLifetimeReason.BackgroundSync)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason),
                reason,
                "A runtime lease must represent exactly one lifetime reason.");
        }
    }
}
