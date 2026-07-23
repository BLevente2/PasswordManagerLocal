using PasswordManagerLocal.Windows.EndpointRpc.Client;

namespace PasswordManagerLocal.Windows.AgentConnection;

public interface IWindowsAgentControlConnection : IEndpointRpcAgentConnection
{
    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
}
