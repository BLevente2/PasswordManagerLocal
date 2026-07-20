using PasswordManagerLocal.Backend.Abstractions;

namespace PasswordManagerLocal.Backend.Hosting;

internal sealed class InteractiveBackendSession : IInteractiveBackendSession
{
    private readonly Func<InteractiveBackendSession, ValueTask> _release;
    private readonly object _operationLock = new();
    private readonly TaskCompletionSource _disposeCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SessionBoundEndpoints _sessionEndpoints;
    private IEndpoints? _endpoints;
    private TaskCompletionSource? _operationsDrained;
    private int _activeOperations;
    private int _releaseStarted;
    private bool _closing;

    public InteractiveBackendSession(
        IEndpoints endpoints,
        Func<InteractiveBackendSession, ValueTask> release)
    {
        _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
        _release = release ?? throw new ArgumentNullException(nameof(release));
        _sessionEndpoints = new SessionBoundEndpoints(this);
    }

    public IEndpoints Endpoints
    {
        get
        {
            lock (_operationLock)
            {
                if (_closing)
                    throw new ObjectDisposedException(nameof(InteractiveBackendSession));

                return _sessionEndpoints;
            }
        }
    }

    internal async Task ExecuteAsync(Func<IEndpoints, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var endpoints = BeginOperation();
        try
        {
            await operation(endpoints);
        }
        finally
        {
            EndOperation();
        }
    }

    internal async Task<T> ExecuteAsync<T>(Func<IEndpoints, Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var endpoints = BeginOperation();
        try
        {
            return await operation(endpoints);
        }
        finally
        {
            EndOperation();
        }
    }

    internal Task InvalidateAsync() => BeginCloseAsync();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _releaseStarted, 1, 0) != 0)
        {
            await _disposeCompletion.Task;
            return;
        }

        try
        {
            await BeginCloseAsync();
            await _release(this);
            _disposeCompletion.TrySetResult();
            GC.SuppressFinalize(this);
        }
        catch (Exception exception)
        {
            _disposeCompletion.TrySetException(exception);
            throw;
        }
    }

    private IEndpoints BeginOperation()
    {
        lock (_operationLock)
        {
            if (_closing || _endpoints is null)
                throw new ObjectDisposedException(nameof(InteractiveBackendSession));

            _activeOperations++;
            return _endpoints;
        }
    }

    private void EndOperation()
    {
        TaskCompletionSource? operationsDrained = null;
        lock (_operationLock)
        {
            _activeOperations--;
            if (_closing && _activeOperations == 0)
            {
                _endpoints = null;
                operationsDrained = _operationsDrained;
                _operationsDrained = null;
            }
        }

        operationsDrained?.TrySetResult();
    }

    private Task BeginCloseAsync()
    {
        lock (_operationLock)
        {
            _closing = true;
            if (_activeOperations == 0)
            {
                _endpoints = null;
                return Task.CompletedTask;
            }

            _operationsDrained ??= new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return _operationsDrained.Task;
        }
    }
}
