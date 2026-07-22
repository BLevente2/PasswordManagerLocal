namespace PasswordManagerLocal.Windows.EndpointRpc.Server;

public interface IEndpointRpcServerSession : IAsyncDisposable
{
    Task RunAsync(CancellationToken cancellationToken = default);
}
