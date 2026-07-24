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

    public event EventHandler? ActiveReasonsChanged;

    public BackendLifetimeReason ActiveReasons
    {
        get
        {
            lock (_stateLock)
                return GetActiveReasonsLocked();
        }
    }

    public async Task<IBackendRuntimeLease> AcquireAsync(
        BackendLifetimeReason reason,
        CancellationToken cancellationToken = default)
    {
        ValidateReason(reason);
        await _transitionLock.WaitAsync(cancellationToken);
        var shouldStopAfterCancellation = false;
        var reasonsChanged = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (GetTotalLeaseCount() == 0)
            {
                shouldStopAfterCancellation = true;
                await _runtime.EnsureStartedAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }

            reasonsChanged = Increment(reason);
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
            if (reasonsChanged)
                PublishActiveReasonsChanged();
        }
    }

    public async Task<IBackendRuntimeLease> ResetDatabaseAndAcquireAsync(
        BackendLifetimeReason reason,
        CancellationToken cancellationToken = default)
    {
        ValidateReason(reason);
        await _transitionLock.WaitAsync(cancellationToken);
        var shouldStopAfterCancellation = false;
        var reasonsChanged = false;

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

            reasonsChanged = Increment(reason);
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
            if (reasonsChanged)
                PublishActiveReasonsChanged();
        }
    }

    public async Task RecoverRuntimeAsync(CancellationToken cancellationToken = default)
    {
        await _transitionLock.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _runtime.StopAsync(CancellationToken.None);

            if (GetTotalLeaseCount() > 0)
                await _runtime.EnsureStartedAsync(CancellationToken.None);
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    internal async ValueTask ReleaseAsync(BackendLifetimeReason reason)
    {
        await _transitionLock.WaitAsync(CancellationToken.None);
        var reasonsChanged = false;
        try
        {
            reasonsChanged = Decrement(reason);
            if (GetTotalLeaseCount() == 0)
                await _runtime.StopAsync(CancellationToken.None);
        }
        finally
        {
            _transitionLock.Release();
            if (reasonsChanged)
                PublishActiveReasonsChanged();
        }
    }

    private int GetTotalLeaseCount()
    {
        lock (_stateLock)
            return _interactiveUiCount + _backgroundSyncCount;
    }

    private bool Increment(BackendLifetimeReason reason)
    {
        lock (_stateLock)
        {
            var previous = GetActiveReasonsLocked();
            if (reason == BackendLifetimeReason.InteractiveUi)
                _interactiveUiCount++;
            else
                _backgroundSyncCount++;
            return previous != GetActiveReasonsLocked();
        }
    }

    private bool Decrement(BackendLifetimeReason reason)
    {
        lock (_stateLock)
        {
            var previous = GetActiveReasonsLocked();
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
            return previous != GetActiveReasonsLocked();
        }
    }

    private BackendLifetimeReason GetActiveReasonsLocked()
    {
        var reasons = BackendLifetimeReason.None;
        if (_interactiveUiCount > 0)
            reasons |= BackendLifetimeReason.InteractiveUi;
        if (_backgroundSyncCount > 0)
            reasons |= BackendLifetimeReason.BackgroundSync;
        return reasons;
    }

    private void PublishActiveReasonsChanged()
    {
        var handlers = ActiveReasonsChanged;
        if (handlers is null)
            return;

        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try { handler(this, EventArgs.Empty); } catch { }
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
