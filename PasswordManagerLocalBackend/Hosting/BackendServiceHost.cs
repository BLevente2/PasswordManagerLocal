using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocalBackend.Abstractions.Services;

namespace PasswordManagerLocalBackend.Hosting;

public sealed class BackendServiceHost : IAsyncDisposable, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly List<IBackendHostedService> _startedServices = [];
    private bool _started;
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
            if (_disposed)
                throw new ObjectDisposedException(nameof(BackendServiceHost));

            if (_started)
                return;

            try
            {
                foreach (var hostedService in _services.GetServices<IBackendHostedService>())
                {
                    await hostedService.StartAsync(cancellationToken);
                    _startedServices.Add(hostedService);
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

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (_disposed || (!_started && _startedServices.Count == 0))
                return;

            await StopStartedServicesAsync(cancellationToken);
            _started = false;
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
                await StopStartedServicesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                disposeException = ex;
            }

            _started = false;
            _disposed = true;

            try
            {
                if (_services is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync();
                else if (_services is IDisposable disposable)
                    disposable.Dispose();
            }
            catch (Exception ex)
            {
                disposeException = disposeException is null
                    ? ex
                    : new AggregateException(disposeException, ex);
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

    private async Task StopStartedServicesAsync(CancellationToken cancellationToken)
    {
        Exception? firstException = null;

        for (var index = _startedServices.Count - 1; index >= 0; index--)
        {
            try
            {
                await _startedServices[index].StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                firstException ??= ex;
            }
        }

        _startedServices.Clear();

        if (firstException is not null)
            throw firstException;
    }
}
