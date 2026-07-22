using PasswordManagerLocal.Windows.EndpointRpc.Serialization;
using PasswordManagerLocal.Windows.Ipc.Client;
using PasswordManagerLocal.Windows.Ipc.Protocol;
using PasswordManagerLocal.Windows.Ipc.Serialization;
using PasswordManagerLocal.Windows.Ipc.Transport;

namespace PasswordManagerLocal.Windows.EndpointRpc.Client;

public sealed class WindowsNamedPipeEndpointRpcConnector : IEndpointRpcClientConnector
{
    public const int MaximumPendingEndpointRequests = 32;

    private readonly string _pipeName;
    private readonly int _processId;
    private readonly Guid _sessionId;

    public WindowsNamedPipeEndpointRpcConnector(
        string pipeName,
        int processId,
        Guid sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId));
        if (sessionId == Guid.Empty)
            throw new ArgumentException("The endpoint session ID cannot be empty.", nameof(sessionId));

        _pipeName = pipeName;
        _processId = processId;
        _sessionId = sessionId;
    }

    public async Task<IEndpointRpcTransport> ConnectAsync(
        CancellationToken cancellationToken = default)
    {
        var pipeClient = new WindowsNamedPipeClient(
            _pipeName,
            new IpcFrameCodec());
        var connection = await pipeClient.ConnectAsync(cancellationToken);
        WindowsIpcClient? client = null;
        try
        {
            client = new WindowsIpcClient(
                connection,
                new WindowsIpcSerializer(),
                new WindowsIpcClientOptions(
                    IpcPeerRole.Ui,
                    IpcPeerRole.Agent,
                    IpcCapabilities.EndpointRpc,
                    _processId,
                    _sessionId,
                    MaximumPendingEndpointRequests,
                    requiredServerCapabilities: IpcCapabilities.EndpointRpc));
            await client.HandshakeAsync(cancellationToken);
            return new WindowsEndpointRpcTransport(
                client,
                new EndpointRpcMessageCodec(new EndpointRpcSerializer()));
        }
        catch (Exception exception)
        {
            var connectionFailure = exception is IpcRemoteException remoteException
                ? EndpointRpcTransportErrorMapper.Map(remoteException.Error)
                : exception;
            Exception? disposalFailure = null;
            try
            {
                if (client is not null)
                    await client.DisposeAsync();
                else
                    await connection.DisposeAsync();
            }
            catch (Exception cleanupException)
            {
                disposalFailure = cleanupException;
            }

            if (disposalFailure is not null)
                throw new AggregateException(connectionFailure, disposalFailure);
            if (!ReferenceEquals(connectionFailure, exception))
                throw connectionFailure;
            throw;
        }
    }
}
