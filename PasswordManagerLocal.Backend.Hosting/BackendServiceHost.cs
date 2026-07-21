using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocal.Backend.Abstractions.Services;

namespace PasswordManagerLocal.Backend.Hosting;

internal sealed class BackendServiceHost : IAsyncDisposable, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly List<IBackendHostedService> _startedServices = [];
    private readonly List<IInteractiveBackendHostedService> _startedInteractiveServices = [];
    private bool _started;
    private bool _interactiveStarted;
    private bool _disposed;

    public BackendServiceHost(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IServiceProvider Services => !_disposed
        ? _services
        : throw new ObjectDisposedException(nameof(BackendServiceHost));

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();

            if (_started)
                return;

            try
            {
                foreach (var hostedService in _services.GetServices<IBackendHostedService>())
                {
                    _startedServices.Add(hostedService);
                    await hostedService.StartAsync(cancellationToken);
                }

                _started = true;
            }
            catch (Exception startException)
            {
                try
                {
                    await StopStartedServicesAsync(CancellationToken.None);
                }
                catch (Exception stopException)
                {
                    throw new AggregateException(startException, stopException);
                }

                throw;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StartInteractiveAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();

            if (!_started)
                throw new InvalidOperationException("The backend core must be started before interactive services.");

            if (_interactiveStarted)
                return;

            try
            {
                foreach (var hostedService in _services.GetServices<IInteractiveBackendHostedService>())
                {
                    _startedInteractiveServices.Add(hostedService);
                    await hostedService.StartAsync(cancellationToken);
                }

                _interactiveStarted = true;
            }
            catch (Exception startException)
            {
                try
                {
                    await StopStartedInteractiveServicesAsync(CancellationToken.None);
                }
                catch (Exception stopException)
                {
                    throw new AggregateException(startException, stopException);
                }

                throw;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopInteractiveAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (_disposed || (!_interactiveStarted && _startedInteractiveServices.Count == 0))
                return;

            await StopStartedInteractiveServicesAsync(cancellationToken);
            _interactiveStarted = false;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (_disposed ||
                (!_started && _startedServices.Count == 0 &&
                 !_interactiveStarted && _startedInteractiveServices.Count == 0))
            {
                return;
            }

            Exception? failure = null;
            try
            {
                await StopStartedInteractiveServicesAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            try
            {
                await StopStartedServicesAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                failure = failure is null
                    ? exception
                    : new AggregateException(failure, exception);
            }

            _interactiveStarted = false;
            _started = false;

            if (failure is not null)
                throw failure;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleLock.WaitAsync();
        Exception? disposeException = null;

        try
        {
            if (_disposed)
                return;

            try
            {
                await StopStartedInteractiveServicesAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                disposeException = exception;
            }

            try
            {
                await StopStartedServicesAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                disposeException = disposeException is null
                    ? exception
                    : new AggregateException(disposeException, exception);
            }

            _interactiveStarted = false;
            _started = false;
            _disposed = true;

            try
            {
                if (_services is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync();
                else if (_services is IDisposable disposable)
                    disposable.Dispose();
            }
            catch (Exception exception)
            {
                disposeException = disposeException is null
                    ? exception
                    : new AggregateException(disposeException, exception);
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }

        GC.SuppressFinalize(this);

        if (disposeException is not null)
            throw disposeException;
    }

    private async Task StopStartedInteractiveServicesAsync(CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();

        for (var index = _startedInteractiveServices.Count - 1; index >= 0; index--)
        {
            try
            {
                await _startedInteractiveServices[index].StopAsync(cancellationToken);
                _startedInteractiveServices.RemoveAt(index);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        ThrowStopFailures(failures);
    }

    private async Task StopStartedServicesAsync(CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();

        for (var index = _startedServices.Count - 1; index >= 0; index--)
        {
            try
            {
                await _startedServices[index].StopAsync(cancellationToken);
                _startedServices.RemoveAt(index);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        ThrowStopFailures(failures);
    }

    private static void ThrowStopFailures(IReadOnlyList<Exception> failures)
    {
        if (failures.Count == 1)
            throw failures[0];
        if (failures.Count > 1)
            throw new AggregateException(failures);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BackendServiceHost));
    }
}
