namespace PasswordManagerLocal.Windows.AgentConnection;

public interface IWindowsAgentControlConnector
{
    Task<IWindowsAgentRegisteredConnection?> TryConnectAndRegisterAsync(
        string pipeName,
        TimeSpan connectTimeout,
        CancellationToken cancellationToken = default);
}
