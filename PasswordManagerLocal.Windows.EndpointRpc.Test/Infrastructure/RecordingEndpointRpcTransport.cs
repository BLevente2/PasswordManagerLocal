using PasswordManagerLocal.Windows.EndpointRpc.Client;
using PasswordManagerLocal.Windows.EndpointRpc.Contracts;

namespace PasswordManagerLocal.Windows.EndpointRpc.Test.Infrastructure;

public sealed class RecordingEndpointRpcTransport : IEndpointRpcTransport
{
    private readonly Func<EndpointOperationId, byte[], CancellationToken, Task<byte[]>> _handler;
    private bool _disposed;

    public RecordingEndpointRpcTransport(
        Func<EndpointOperationId, byte[], CancellationToken, Task<byte[]>> handler) =>
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));

    public List<EndpointOperationId> Operations { get; } = [];
    public List<byte[]> RequestPayloads { get; } = [];
    public bool IsConnected => !_disposed;
    public Task Completion { get; } = Task.Delay(Timeout.InfiniteTimeSpan);

    public async Task<byte[]> SendAsync(
        EndpointOperationId operationId,
        byte[] requestPayload,
        EndpointOperationCancellationClassification cancellationClassification,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new EndpointRpcDisconnectedException();
        Operations.Add(operationId);
        RequestPayloads.Add(requestPayload.ToArray());
        return await _handler(operationId, requestPayload, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
