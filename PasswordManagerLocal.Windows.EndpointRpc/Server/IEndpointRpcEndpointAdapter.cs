using PasswordManagerLocal.Backend.Abstractions;

namespace PasswordManagerLocal.Windows.EndpointRpc.Server;

public interface IEndpointRpcEndpointAdapter
{
    IEndpoints GetEndpoints(EndpointRequestContext context);
}
