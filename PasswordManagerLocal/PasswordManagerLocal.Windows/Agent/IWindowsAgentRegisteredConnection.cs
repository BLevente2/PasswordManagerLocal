namespace PasswordManagerLocal.Windows.AgentConnection;

public interface IWindowsAgentRegisteredConnection : IAsyncDisposable
{
    bool IsConnected { get; }
}
