using PasswordManagerLocal.Windows.EndpointRpc.Contracts;
using PasswordManagerLocal.Windows.EndpointRpc.Security;
using PasswordManagerLocal.Windows.EndpointRpc.Serialization;
using PasswordManagerLocal.Windows.Ipc.Client;
using PasswordManagerLocal.Windows.Ipc.Protocol;
using PasswordManagerLocal.Windows.Ipc.Transport;

namespace PasswordManagerLocal.Windows.EndpointRpc.Client;

public sealed class WindowsEndpointRpcTransport : IEndpointRpcTransport
{
    private readonly WindowsIpcClient _client;
    private readonly EndpointRpcMessageCodec _messageCodec;
    private int _disposed;

    public WindowsEndpointRpcTransport(
        WindowsIpcClient client,
        EndpointRpcMessageCodec messageCodec)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _messageCodec = messageCodec ?? throw new ArgumentNullException(nameof(messageCodec));
    }

    public bool IsConnected => Volatile.Read(ref _disposed) == 0 && _client.IsConnected;
    public Task Completion => _client.Completion;

    public async Task<byte[]> SendAsync(
        EndpointOperationId operationId,
        byte[] requestPayload,
        EndpointOperationCancellationClassification cancellationClassification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestPayload);
        if (!Enum.IsDefined(cancellationClassification))
            throw new ArgumentOutOfRangeException(nameof(cancellationClassification));

        ThrowIfDisposed();
        if (!IsConnected)
            throw new EndpointRpcDisconnectedException();

        cancellationToken.ThrowIfCancellationRequested();
        var encodedRequest = _messageCodec.EncodeRequest(operationId, requestPayload);
        byte[]? encodedResponse = null;
        try
        {
            var response = await _client.SendAsync(
                IpcOperationId.EndpointRpcRequest,
                encodedRequest,
                cancellationToken);
            encodedResponse = response.Result
                ?? throw new EndpointRpcPayloadException("The endpoint RPC response payload is missing.");
            return _messageCodec.DecodeResponse(encodedResponse);
        }
        catch (IpcConnectionClosedException exception)
        {
            throw new EndpointRpcDisconnectedException(exception);
        }
        catch (IpcRemoteException exception)
        {
            throw EndpointRpcTransportErrorMapper.Map(exception.Error);
        }
        finally
        {
            EndpointSensitiveData.Clear(encodedRequest);
            EndpointSensitiveData.Clear(encodedResponse);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _client.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(WindowsEndpointRpcTransport));
    }
}
