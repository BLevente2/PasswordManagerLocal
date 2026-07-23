using PasswordManagerLocal.Windows.Ipc.Client;
using PasswordManagerLocal.Windows.Ipc.Contracts;

namespace PasswordManagerLocal.Windows.AgentConnection;

public sealed class WindowsAgentRegisteredConnection : IWindowsAgentRegisteredConnection
{
    private readonly WindowsIpcClient _client;
    private readonly WindowsIpcControlClient _controlClient;
    private int _disposed;

    public WindowsAgentRegisteredConnection(
        WindowsIpcClient client,
        WindowsIpcControlClient controlClient)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _controlClient = controlClient ?? throw new ArgumentNullException(nameof(controlClient));
    }

    public bool IsConnected => _client.IsConnected && Volatile.Read(ref _disposed) == 0;
    public int? AgentProcessId => _client.VerifiedServerProcessId;
    public Task Completion => _client.Completion;

    public Task<BackendRuntimeStatusDto> GetBackendRuntimeStatusAsync(
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(WindowsAgentRegisteredConnection));
        return _controlClient.GetBackendRuntimeStatusAsync(cancellationToken);
    }

    public Task<DatabaseResetResultDto> ResetDatabaseAsync(
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(WindowsAgentRegisteredConnection));
        return _controlClient.ResetDatabaseAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            await _controlClient.UnregisterUiConnectionAsync();
        }
        catch
        {
        }

        try
        {
            await _client.DisposeAsync();
        }
        catch
        {
        }
    }
}
