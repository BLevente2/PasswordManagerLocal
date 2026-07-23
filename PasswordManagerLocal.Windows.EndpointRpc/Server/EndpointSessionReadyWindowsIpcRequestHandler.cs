using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Protocol;
using PasswordManagerLocal.Windows.Ipc.Serialization;
using PasswordManagerLocal.Windows.Ipc.Server;

namespace PasswordManagerLocal.Windows.EndpointRpc.Server;

public sealed class EndpointSessionReadyWindowsIpcRequestHandler : IWindowsIpcRequestHandler
{
    private readonly IEndpointRpcSessionReadiness _readiness;

    public EndpointSessionReadyWindowsIpcRequestHandler(IEndpointRpcSessionReadiness readiness) =>
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));

    public IpcOperationId OperationId => IpcOperationId.EndpointSessionReady;

    public Task<IpcResponseEnvelope> HandleAsync(
        IpcRequestContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        context.EnsureNoPayload();
        return Task.FromResult(context.Success(
            new RequestAcceptedDto(_readiness.IsReady(context.Connection)),
            WindowsIpcJsonContext.Default.RequestAcceptedDto));
    }
}
