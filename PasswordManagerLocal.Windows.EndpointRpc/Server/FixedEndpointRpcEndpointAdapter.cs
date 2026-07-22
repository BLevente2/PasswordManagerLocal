using PasswordManagerLocal.Backend.Abstractions;

namespace PasswordManagerLocal.Windows.EndpointRpc.Server;

public sealed class FixedEndpointRpcEndpointAdapter : IEndpointRpcEndpointAdapter
{
    private readonly IEndpoints _endpoints;

    public FixedEndpointRpcEndpointAdapter(IEndpoints endpoints) =>
        _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));

    public IEndpoints GetEndpoints(EndpointRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _endpoints;
    }
}
