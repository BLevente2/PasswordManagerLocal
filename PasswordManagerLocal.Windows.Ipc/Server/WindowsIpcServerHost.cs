using PasswordManagerLocal.Windows.Ipc.Transport;
using System.Collections.Concurrent;

namespace PasswordManagerLocal.Windows.Ipc.Server;

public sealed class WindowsIpcServerHost : IWindowsIpcServerHost
{
    private readonly IWindowsIpcConnectionListener _listener;
    private readonly IWindowsIpcServerSessionFactory _sessionFactory;
    private readonly SemaphoreSlim _connectionCapacity;
    private readonly ConcurrentDictionary<IWindowsIpcServerSession, Task> _activeSessions = new();
    private readonly CancellationTokenSource _shutdownSource = new();
    private readonly object _gate = new();
    private Task? _acceptLoopTask;
    private Task? _stopTask;
    private Exception? _listenerFailure;
    private int _started;

    public WindowsIpcServerHost(
        IWindowsIpcConnectionListener listener,
        IWindowsIpcServerSessionFactory sessionFactory,
        WindowsIpcServerHostOptions? options = null)
    {
        _listener = listener ?? throw new ArgumentNullException(nameof(listener));
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        var resolvedOptions = options ?? new WindowsIpcServerHostOptions();
        _connectionCapacity = new SemaphoreSlim(
            resolvedOptions.MaximumActiveConnections,
            resolvedOptions.MaximumActiveConnections);
    }

    public int ActiveSessionCount => _activeSessions.Count;
    public Exception? ListenerFailure => Volatile.Read(ref _listenerFailure);
    public Task Completion => Volatile.Read(ref _acceptLoopTask) ?? Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("The IPC server host has already started.");

        _acceptLoopTask = RunAcceptLoopAsync(_shutdownSource.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task stopTask;
        lock (_gate)
            stopTask = _stopTask ??= StopCoreAsync();

        return cancellationToken.CanBeCanceled
            ? stopTask.WaitAsync(cancellationToken)
            : stopTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _shutdownSource.Dispose();
        _connectionCapacity.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task RunAcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var connection = await _listener.AcceptAsync(cancellationToken);
                if (!_connectionCapacity.Wait(0))
                {
                    try
                    {
                        await connection.DisposeAsync();
                    }
                    catch
                    {
                    }
                    continue;
                }

                IWindowsIpcServerSession session;
                try
                {
                    session = _sessionFactory.Create(connection);
                }
                catch
                {
                    _connectionCapacity.Release();
                    try
                    {
                        await connection.DisposeAsync();
                    }
                    catch
                    {
                    }
                    continue;
                }

                var runTask = RunSessionAsync(session, cancellationToken);
                if (!_activeSessions.TryAdd(session, runTask))
                {
                    _connectionCapacity.Release();
                    try
                    {
                        await session.DisposeAsync();
                    }
                    catch
                    {
                    }
                    continue;
                }

                _ = ObserveSessionAsync(session, runTask);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _listenerFailure, exception);
            throw;
        }
    }

    private static async Task RunSessionAsync(
        IWindowsIpcServerSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            await session.RunAsync(cancellationToken);
        }
        catch
        {
        }
    }

    private async Task ObserveSessionAsync(
        IWindowsIpcServerSession session,
        Task runTask)
    {
        try
        {
            await runTask;
        }
        finally
        {
            if (_activeSessions.TryRemove(session, out _))
                _connectionCapacity.Release();

            try
            {
                await session.DisposeAsync();
            }
            catch
            {
            }
        }
    }

    private async Task StopCoreAsync()
    {
        _shutdownSource.Cancel();
        var acceptLoop = Completion;
        try
        {
            await acceptLoop;
        }
        catch
        {
        }

        while (_activeSessions.Count > 0)
        {
            var snapshot = _activeSessions.Keys.ToArray();
            if (snapshot.Length == 0)
                break;

            foreach (var session in snapshot)
            {
                try
                {
                    await session.DisposeAsync();
                }
                catch
                {
                }
            }

            var tasks = snapshot
                .Select(session => _activeSessions.TryGetValue(session, out var task) ? task : Task.CompletedTask)
                .ToArray();
            await Task.WhenAll(tasks);
        }
    }
}
