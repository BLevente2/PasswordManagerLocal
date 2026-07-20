using PasswordManagerLocal.Runtime.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Backend.Abstractions.Security;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Abstractions.Sync.Discovery;
using PasswordManagerLocal.Backend.DependencyInjection;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync.Enrollment.Diagnostics;
using SQLitePCL;

namespace PasswordManagerLocal.Backend.Hosting;

internal sealed class BackendRuntime : IBackendRuntime
{
    private readonly BackendRuntimeOptions _options;
    private readonly BackendStorageCleaner _storageCleaner;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private BackendRuntimeSnapshot _snapshot = new(
        BackendRuntimeState.NotStarted,
        BackendRuntimeFailureKind.None,
        null,
        DateTimeOffset.UtcNow);
    private SyncRuntimeSnapshot _syncSnapshot = new(SyncRuntimeState.Disabled, null);
    private BackendServiceHost? _host;
    private ISyncRuntimeService? _syncRuntime;
    private Task? _startupTask;
    private Task? _stopTask;
    private long _hostGeneration;
    private bool _disposeRequested;
    private bool _disposed;

    public BackendRuntime(BackendRuntimeOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _storageCleaner = new BackendStorageCleaner(options.StoragePaths);
    }

    public BackendRuntimeSnapshot Snapshot
    {
        get
        {
            lock (_gate)
                return _snapshot;
        }
    }

    public SyncRuntimeSnapshot SyncSnapshot
    {
        get
        {
            lock (_gate)
                return _syncSnapshot;
        }
    }

    public event EventHandler<BackendRuntimeStateChangedEventArgs>? StateChanged;
    public event EventHandler<SyncRuntimeStateChangedEventArgs>? SyncStateChanged;

    public async Task EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task operation;
            TaskCompletionSource? starter = null;
            BackendRuntimeStateChangedEventArgs? stateChange = null;
            var retryAfterOperation = false;

            lock (_gate)
            {
                ThrowIfDisposedLocked();

                if (_snapshot.State == BackendRuntimeState.Ready)
                    return;

                if (_stopTask is not null)
                {
                    operation = _stopTask;
                    retryAfterOperation = true;
                }
                else if (_startupTask is not null)
                {
                    operation = _startupTask;
                }
                else
                {
                    starter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _startupTask = starter.Task;
                    operation = starter.Task;
                    stateChange = TransitionLocked(
                        BackendRuntimeState.Starting,
                        BackendRuntimeFailureKind.None,
                        null);
                }
            }

            Publish(stateChange);

            if (starter is not null)
                _ = RunStartupAsync(starter, resetStorageFirst: false);

            await operation.WaitAsync(cancellationToken);
            if (!retryAfterOperation)
                return;
        }
    }

    public async Task WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync(cancellationToken);

        var snapshot = Snapshot;
        if (!snapshot.IsReady)
            throw snapshot.Failure ?? new InvalidOperationException("The backend runtime is not ready.");
    }

    public async Task<IEndpoints> GetEndpointsAsync(CancellationToken cancellationToken = default)
    {
        await WaitUntilReadyAsync(cancellationToken);

        lock (_gate)
        {
            if (_host is null || _snapshot.State != BackendRuntimeState.Ready)
                throw new InvalidOperationException("The backend runtime is not ready.");

            return _host.Services.GetRequiredService<IEndpoints>();
        }
    }

    public Task ResetDatabaseAndRestartAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource starter;
        BackendRuntimeStateChangedEventArgs? stateChange;

        lock (_gate)
        {
            ThrowIfDisposedLocked();

            if (_startupTask is not null || _stopTask is not null)
                throw new InvalidOperationException("A backend lifecycle operation is already in progress.");

            if (_snapshot.State != BackendRuntimeState.Failed ||
                _snapshot.FailureKind != BackendRuntimeFailureKind.DatabaseCompatibility)
            {
                throw new InvalidOperationException(
                    "The database can only be reset after a database compatibility startup failure.");
            }

            starter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _startupTask = starter.Task;
            stateChange = TransitionLocked(
                BackendRuntimeState.Starting,
                BackendRuntimeFailureKind.None,
                null);
        }

        Publish(stateChange);
        _ = RunStartupAsync(starter, resetStorageFirst: true);
        return starter.Task.WaitAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task? startupToWait;
            Task operation;
            TaskCompletionSource? stopper = null;
            BackendRuntimeStateChangedEventArgs? stateChange = null;

            lock (_gate)
            {
                if (_disposed)
                    return;

                startupToWait = _startupTask;
                if (startupToWait is not null)
                {
                    operation = startupToWait;
                }
                else if (_stopTask is not null)
                {
                    operation = _stopTask;
                }
                else if (_snapshot.State is BackendRuntimeState.NotStarted or BackendRuntimeState.Stopped)
                {
                    return;
                }
                else
                {
                    stopper = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _stopTask = stopper.Task;
                    operation = stopper.Task;
                    stateChange = TransitionLocked(
                        BackendRuntimeState.Stopping,
                        BackendRuntimeFailureKind.None,
                        null);
                }
            }

            Publish(stateChange);

            if (stopper is not null)
                _ = RunStopAsync(stopper);

            try
            {
                await operation.WaitAsync(cancellationToken);
            }
            catch when (startupToWait is not null && !cancellationToken.IsCancellationRequested)
            {
            }

            if (startupToWait is null)
                return;
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposeRequested = true;
        }

        await StopAsync(CancellationToken.None);

        lock (_gate)
            _disposed = true;

        GC.SuppressFinalize(this);
    }

    private async Task RunStartupAsync(TaskCompletionSource completion, bool resetStorageFirst)
    {
        BackendServiceHost? newHost = null;
        ISyncRuntimeService? newSyncRuntime = null;
        BackendRuntimeStateChangedEventArgs? finalStateChange = null;
        SyncRuntimeStateChangedEventArgs? syncStateChange = null;
        Exception? failure = null;
        long generation = 0;
        var lifecycleLockTaken = false;

        try
        {
            await _lifecycleLock.WaitAsync(CancellationToken.None);
            lifecycleLockTaken = true;

            await DisposeCurrentHostCoreAsync();

            if (resetStorageFirst)
                _storageCleaner.DeleteDatabaseFiles();

            (newHost, newSyncRuntime) = await CreateAndStartHostAsync();

            lock (_gate)
            {
                _host = newHost;
                _syncRuntime = newSyncRuntime;
                newSyncRuntime.StateChanged += HandleSyncStateChanged;
                generation = ++_hostGeneration;
                syncStateChange = SetSyncSnapshotLocked(newSyncRuntime.Snapshot);
                finalStateChange = TransitionLocked(
                    BackendRuntimeState.Ready,
                    BackendRuntimeFailureKind.None,
                    null);
                _startupTask = null;
            }

            newHost = null;
            newSyncRuntime = null;
        }
        catch (Exception exception)
        {
            failure = exception;

            if (newSyncRuntime is not null)
                newSyncRuntime.StateChanged -= HandleSyncStateChanged;

            if (newHost is not null)
            {
                try
                {
                    await newHost.DisposeAsync();
                }
                catch (Exception disposeException)
                {
                    failure = new AggregateException(failure, disposeException);
                }
            }

            if (resetStorageFirst)
            {
                try
                {
                    _storageCleaner.DeleteDatabaseFiles();
                }
                catch (Exception cleanupException)
                {
                    failure = new AggregateException(failure, cleanupException);
                }
            }

            var (state, failureKind) = ClassifyFailure(failure);
            lock (_gate)
            {
                _host = null;
                _syncRuntime = null;
                finalStateChange = TransitionLocked(state, failureKind, failure);
                syncStateChange = SetSyncSnapshotLocked(
                    new SyncRuntimeSnapshot(SyncRuntimeState.Disabled, null));
                _startupTask = null;
            }
        }
        finally
        {
            if (lifecycleLockTaken)
                _lifecycleLock.Release();
        }

        Publish(syncStateChange);
        Publish(finalStateChange);

        if (failure is null)
        {
            completion.TrySetResult();
            _ = RunSyncStartupAsync(generation);
        }
        else
        {
            completion.TrySetException(failure);
        }
    }

    private async Task<(BackendServiceHost Host, ISyncRuntimeService SyncRuntime)> CreateAndStartHostAsync()
    {
        Batteries_V2.Init();
        DeviceEnrollmentTrace.InitializeForCurrentBuild(_options.StoragePaths.EnrollmentLogPath);

        var keyProtector = _options.KeyProtectorFactory()
            ?? throw new InvalidOperationException("The platform key-protector factory returned null.");
        var discoveryLease = _options.DiscoveryNetworkLeaseFactory()
            ?? throw new InvalidOperationException("The platform discovery-lease factory returned null.");

        BackendServiceHost host;
        if (_options.ServiceHostFactory is not null)
        {
            host = _options.ServiceHostFactory(keyProtector, discoveryLease)
                ?? throw new InvalidOperationException("The backend service-host factory returned null.");
        }
        else
        {
            var services = new ServiceCollection();
            services.AddPasswordManagerLocalBackend(
                _options.StoragePaths,
                keyProtector,
                discoveryLease);
            host = new BackendServiceHost(services.BuildServiceProvider());
        }
        try
        {
            await host.Services
                .GetRequiredService<IBackendInitializationService>()
                .InitializeAsync(CancellationToken.None);
            await host.StartAsync(CancellationToken.None);

            var syncRuntime = host.Services.GetRequiredService<ISyncRuntimeService>();
            return (host, syncRuntime);
        }
        catch
        {
            try
            {
                await host.DisposeAsync();
            }
            catch
            {
            }

            throw;
        }
    }

    private async Task RunSyncStartupAsync(long generation)
    {
        await _lifecycleLock.WaitAsync(CancellationToken.None);
        try
        {
            ISyncRuntimeService? syncRuntime;
            lock (_gate)
            {
                if (_disposed ||
                    _hostGeneration != generation ||
                    _snapshot.State != BackendRuntimeState.Ready)
                {
                    return;
                }

                syncRuntime = _syncRuntime;
            }

            if (syncRuntime is null)
                return;

            try
            {
                await syncRuntime.RefreshSyncEnabledAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                SyncRuntimeStateChangedEventArgs? stateChange;
                lock (_gate)
                {
                    if (_syncRuntime != syncRuntime || _hostGeneration != generation)
                        return;

                    stateChange = SetSyncSnapshotLocked(
                        new SyncRuntimeSnapshot(SyncRuntimeState.Degraded, exception));
                }

                Publish(stateChange);
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task RunStopAsync(TaskCompletionSource completion)
    {
        BackendRuntimeStateChangedEventArgs? stateChange = null;
        SyncRuntimeStateChangedEventArgs? syncStateChange = null;
        Exception? failure = null;

        await _lifecycleLock.WaitAsync(CancellationToken.None);
        try
        {
            try
            {
                await DisposeCurrentHostCoreAsync();
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            lock (_gate)
            {
                _hostGeneration++;
                _host = null;
                _syncRuntime = null;
                syncStateChange = SetSyncSnapshotLocked(new SyncRuntimeSnapshot(SyncRuntimeState.Disabled, null));
                stateChange = TransitionLocked(
                    BackendRuntimeState.Stopped,
                    BackendRuntimeFailureKind.None,
                    null);
                _stopTask = null;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }

        Publish(syncStateChange);
        Publish(stateChange);

        if (failure is null)
            completion.TrySetResult();
        else
            completion.TrySetException(failure);
    }

    private async Task DisposeCurrentHostCoreAsync()
    {
        BackendServiceHost? host;
        ISyncRuntimeService? syncRuntime;

        lock (_gate)
        {
            host = _host;
            syncRuntime = _syncRuntime;
            if (syncRuntime is not null)
                syncRuntime.StateChanged -= HandleSyncStateChanged;

            _host = null;
            _syncRuntime = null;
            _hostGeneration++;
        }

        Exception? failure = null;
        if (syncRuntime is not null)
        {
            try
            {
                await syncRuntime.StopAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        if (host is not null)
        {
            try
            {
                await host.DisposeAsync();
            }
            catch (Exception exception)
            {
                failure = failure is null
                    ? exception
                    : new AggregateException(failure, exception);
            }
        }

        if (failure is not null)
            throw failure;
    }

    private void HandleSyncStateChanged(object? sender, SyncRuntimeStateChangedEventArgs args)
    {
        SyncRuntimeStateChangedEventArgs? stateChange;
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _syncRuntime))
                return;

            stateChange = SetSyncSnapshotLocked(args.Current);
        }

        Publish(stateChange);
    }

    private BackendRuntimeStateChangedEventArgs? TransitionLocked(
        BackendRuntimeState state,
        BackendRuntimeFailureKind failureKind,
        Exception? failure)
    {
        if (_snapshot.State == state &&
            _snapshot.FailureKind == failureKind &&
            ReferenceEquals(_snapshot.Failure, failure))
        {
            return null;
        }

        var previous = _snapshot;
        _snapshot = new BackendRuntimeSnapshot(
            state,
            failureKind,
            failure,
            DateTimeOffset.UtcNow);
        return new BackendRuntimeStateChangedEventArgs(previous, _snapshot);
    }

    private SyncRuntimeStateChangedEventArgs? SetSyncSnapshotLocked(SyncRuntimeSnapshot snapshot)
    {
        if (_syncSnapshot == snapshot)
            return null;

        var previous = _syncSnapshot;
        _syncSnapshot = snapshot;
        return new SyncRuntimeStateChangedEventArgs(previous, snapshot);
    }

    private static (BackendRuntimeState State, BackendRuntimeFailureKind FailureKind) ClassifyFailure(
        Exception exception)
    {
        var primaryException = GetPrimaryFailure(exception);
        return primaryException switch
        {
            KeyProtectorUnavailableException =>
                (BackendRuntimeState.WaitingForDeviceUnlock, BackendRuntimeFailureKind.PlatformKeyUnavailable),
            DatabaseVersionNotSupportedException =>
                (BackendRuntimeState.Failed, BackendRuntimeFailureKind.DatabaseCompatibility),
            UnauthorizedAccessException or IOException =>
                (BackendRuntimeState.Failed, BackendRuntimeFailureKind.StorageUnavailable),
            _ =>
                (BackendRuntimeState.Failed, BackendRuntimeFailureKind.StartupFailure)
        };
    }

    private static Exception GetPrimaryFailure(Exception exception)
    {
        while (exception is AggregateException { InnerExceptions.Count: > 0 } aggregate)
            exception = aggregate.InnerExceptions[0];

        return exception;
    }

    private void ThrowIfDisposedLocked()
    {
        if (_disposeRequested || _disposed)
            throw new ObjectDisposedException(nameof(BackendRuntime));
    }

    private void Publish(BackendRuntimeStateChangedEventArgs? stateChange)
    {
        if (stateChange is null)
            return;

        var handlers = StateChanged;
        if (handlers is null)
            return;

        foreach (EventHandler<BackendRuntimeStateChangedEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, stateChange);
            }
            catch
            {
            }
        }
    }

    private void Publish(SyncRuntimeStateChangedEventArgs? stateChange)
    {
        if (stateChange is null)
            return;

        var handlers = SyncStateChanged;
        if (handlers is null)
            return;

        foreach (EventHandler<SyncRuntimeStateChangedEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, stateChange);
            }
            catch
            {
            }
        }
    }
}
