namespace PasswordManagerLocal.Windows.AgentConnection;

public interface IWindowsAgentControlConnection : IAsyncDisposable
{
    bool IsConnected { get; }
    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
}
