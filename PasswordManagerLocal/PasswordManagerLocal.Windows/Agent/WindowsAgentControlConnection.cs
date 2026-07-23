using PasswordManagerLocal.Windows.Ipc.Contracts;

namespace PasswordManagerLocal.Windows.AgentConnection;

public sealed class WindowsAgentControlConnection : IWindowsAgentControlConnection
{
    private readonly string _pipeName;
    private readonly WindowsUiIpcIdentity _identity;
    private readonly IWindowsAgentLauncher _agentLauncher;
    private readonly IWindowsAgentControlConnector _connector;
    private readonly int _maximumConnectionAttempts;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _retryDelay;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private IWindowsAgentRegisteredConnection? _connection;
    private long _connectionGeneration;
    private int _disposed;

    public WindowsAgentControlConnection(
        string pipeName,
        WindowsUiIpcIdentity identity,
        IWindowsAgentLauncher agentLauncher,
        IWindowsAgentControlConnector? connector = null,
        int maximumConnectionAttempts = 6,
        TimeSpan? connectTimeout = null,
        TimeSpan? retryDelay = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _pipeName = pipeName;
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _agentLauncher = agentLauncher ?? throw new ArgumentNullException(nameof(agentLauncher));
        _connector = connector ?? new WindowsAgentControlConnector();
        if (maximumConnectionAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumConnectionAttempts));
        _maximumConnectionAttempts = maximumConnectionAttempts;
        _connectTimeout = connectTimeout ?? TimeSpan.FromMilliseconds(500);
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(250);
        if (_connectTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(connectTimeout));
        if (_retryDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(retryDelay));
    }

    public bool IsConnected => _connection?.IsConnected == true;
    public long ConnectionGeneration => Interlocked.Read(ref _connectionGeneration);
    public Task Completion => _connection?.Completion ?? Task.CompletedTask;
    public int? AgentProcessId => _connection?.AgentProcessId;

    public Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken = default) =>
        ConnectAsync(cancellationToken);

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (IsConnected)
                return true;

            await DisposeCurrentConnectionAsync();
            _connection = await _connector.TryConnectAndRegisterAsync(
                _pipeName,
                _identity,
                _connectTimeout,
                cancellationToken);
            if (_connection is not null)
            {
                Interlocked.Increment(ref _connectionGeneration);
                return true;
            }

            if (!await _agentLauncher.LaunchAsync(cancellationToken))
                return false;

            for (var attempt = 0; attempt < _maximumConnectionAttempts; attempt++)
            {
                if (attempt > 0)
                    await Task.Delay(_retryDelay, cancellationToken);
                _connection = await _connector.TryConnectAndRegisterAsync(
                    _pipeName,
                    _identity,
                    _connectTimeout,
                    cancellationToken);
                if (_connection is not null)
                {
                    Interlocked.Increment(ref _connectionGeneration);
                    return true;
                }
            }

            return false;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task<BackendRuntimeStatusDto> GetBackendRuntimeStatusAsync(
        CancellationToken cancellationToken = default)
    {
        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_connection?.IsConnected != true)
                throw new InvalidOperationException("The Windows agent control connection is unavailable.");
            return await _connection.GetBackendRuntimeStatusAsync(cancellationToken);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task<DatabaseResetResultDto> ResetDatabaseAsync(
        CancellationToken cancellationToken = default)
    {
        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_connection?.IsConnected != true)
                throw new InvalidOperationException("The Windows agent control connection is unavailable.");
            return await _connection.ResetDatabaseAsync(cancellationToken);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _connectionGate.WaitAsync();
        try
        {
            await DisposeCurrentConnectionAsync();
        }
        finally
        {
            _connectionGate.Release();
            _connectionGate.Dispose();
        }
    }

    private async Task DisposeCurrentConnectionAsync()
    {
        var connection = _connection;
        _connection = null;
        if (connection is null)
            return;
        try { await connection.DisposeAsync(); } catch { }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(WindowsAgentControlConnection));
    }
}
