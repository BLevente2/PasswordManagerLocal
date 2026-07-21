namespace PasswordManagerLocal.Windows.AgentConnection;

public sealed class WindowsAgentControlConnection : IWindowsAgentControlConnection
{
    private readonly string _pipeName;
    private readonly IWindowsAgentLauncher _agentLauncher;
    private readonly IWindowsAgentControlConnector _connector;
    private readonly int _maximumConnectionAttempts;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _retryDelay;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private IWindowsAgentRegisteredConnection? _connection;
    private int _disposed;

    public WindowsAgentControlConnection(
        string pipeName,
        IWindowsAgentLauncher agentLauncher,
        IWindowsAgentControlConnector? connector = null,
        int maximumConnectionAttempts = 6,
        TimeSpan? connectTimeout = null,
        TimeSpan? retryDelay = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _pipeName = pipeName;
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

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (IsConnected)
                return true;

            if (_connection is not null)
            {
                try
                {
                    await _connection.DisposeAsync();
                }
                catch
                {
                }
                _connection = null;
            }

            _connection = await _connector.TryConnectAndRegisterAsync(
                _pipeName,
                _connectTimeout,
                cancellationToken);
            if (_connection is not null)
                return true;

            if (!await _agentLauncher.LaunchAsync(cancellationToken))
                return false;

            for (var attempt = 0; attempt < _maximumConnectionAttempts; attempt++)
            {
                if (attempt > 0)
                    await Task.Delay(_retryDelay, cancellationToken);
                _connection = await _connector.TryConnectAndRegisterAsync(
                    _pipeName,
                    _connectTimeout,
                    cancellationToken);
                if (_connection is not null)
                    return true;
            }

            return false;
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
            if (_connection is not null)
                await _connection.DisposeAsync();
            _connection = null;
        }
        finally
        {
            _connectionGate.Release();
            _connectionGate.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(WindowsAgentControlConnection));
    }
}
