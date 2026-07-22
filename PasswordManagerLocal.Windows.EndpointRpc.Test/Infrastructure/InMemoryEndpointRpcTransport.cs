using PasswordManagerLocal.Windows.EndpointRpc.Client;
using PasswordManagerLocal.Windows.EndpointRpc.Contracts;
using PasswordManagerLocal.Windows.EndpointRpc.Security;
using PasswordManagerLocal.Windows.EndpointRpc.Serialization;
using PasswordManagerLocal.Windows.EndpointRpc.Server;
using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Lifecycle;
using PasswordManagerLocal.Windows.Ipc.Protocol;
using PasswordManagerLocal.Windows.Ipc.Serialization;
using PasswordManagerLocal.Windows.Ipc.Server;

namespace PasswordManagerLocal.Windows.EndpointRpc.Test.Infrastructure;

public sealed class InMemoryEndpointRpcTransport : IEndpointRpcTransport
{
    private readonly EndpointRpcWindowsIpcRequestHandler _handler;
    private readonly EndpointRpcMessageCodec _codec;
    private long _correlationId = 1;
    private bool _disposed;

    public InMemoryEndpointRpcTransport(
        EndpointRpcWindowsIpcRequestHandler handler,
        EndpointRpcMessageCodec codec)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
    }

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

        var correlationId = Interlocked.Increment(ref _correlationId);
        var encodedRequest = _codec.EncodeRequest(operationId, requestPayload);
        try
        {
            var connection = new IpcConnectionContext(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                IpcPeerRole.Ui,
                1234,
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                IpcCapabilities.EndpointRpc);
            var envelope = new IpcRequestEnvelope(
                correlationId,
                IpcOperationId.EndpointRpcRequest,
                encodedRequest);
            var context = new IpcRequestContext(
                connection,
                envelope,
                new WindowsIpcSerializer());
            var response = await _handler.HandleAsync(context, cancellationToken);
            if (!response.IsSuccess)
                throw new InvalidOperationException("The in-memory IPC handler returned an outer transport failure.");
            return _codec.DecodeResponse(response.Result!);
        }
        finally
        {
            EndpointSensitiveData.Clear(encodedRequest);
        }
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
