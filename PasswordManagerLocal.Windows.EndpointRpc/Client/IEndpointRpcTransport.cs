using PasswordManagerLocal.Windows.EndpointRpc.Contracts;

namespace PasswordManagerLocal.Windows.EndpointRpc.Client;

public interface IEndpointRpcTransport : IAsyncDisposable
{
    bool IsConnected { get; }
    Task Completion { get; }

    Task<byte[]> SendAsync(
        EndpointOperationId operationId,
        byte[] requestPayload,
        EndpointOperationCancellationClassification cancellationClassification,
        CancellationToken cancellationToken = default);
}
