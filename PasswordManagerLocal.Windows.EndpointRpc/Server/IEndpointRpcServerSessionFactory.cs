using PasswordManagerLocal.Windows.Ipc.Transport;

namespace PasswordManagerLocal.Windows.EndpointRpc.Server;

public interface IEndpointRpcServerSessionFactory
{
    IEndpointRpcServerSession Create(IWindowsIpcConnection connection);
}
