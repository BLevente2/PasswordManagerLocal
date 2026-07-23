using PasswordManagerLocal.Windows.Ipc.Contracts;

namespace PasswordManagerLocal.Windows.AgentConnection;

public interface IWindowsAgentControlConnector
{
    Task<IWindowsAgentRegisteredConnection?> TryConnectAndRegisterAsync(
        string pipeName,
        WindowsUiIpcIdentity identity,
        TimeSpan connectTimeout,
        CancellationToken cancellationToken = default);
}
