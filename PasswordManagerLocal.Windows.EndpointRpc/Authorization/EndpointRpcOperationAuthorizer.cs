using PasswordManagerLocal.Windows.Ipc.Authorization;
using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Protocol;
using PasswordManagerLocal.Windows.Ipc.Server;

namespace PasswordManagerLocal.Windows.EndpointRpc.Authorization;

public sealed class EndpointRpcOperationAuthorizer : IWindowsIpcOperationAuthorizer
{
    private readonly EndpointRpcConnectionAuthorizer _connectionAuthorizer;

    public EndpointRpcOperationAuthorizer(EndpointRpcConnectionAuthorizer connectionAuthorizer) =>
        _connectionAuthorizer = connectionAuthorizer
            ?? throw new ArgumentNullException(nameof(connectionAuthorizer));

    public IpcAuthorizationDecision Authorize(IpcRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!_connectionAuthorizer.IsAuthorizedConnection(context.Connection))
        {
            return IpcAuthorizationDecision.Denied(
                IpcErrorCode.UnauthorizedOperation,
                IpcErrorCategory.Validation,
                "The endpoint operation is not authorized.");
        }

        return context.Request.OperationId == IpcOperationId.EndpointRpcRequest
            ? IpcAuthorizationDecision.Allowed
            : IpcAuthorizationDecision.Denied(
                IpcErrorCode.UnauthorizedOperation,
                IpcErrorCategory.Validation,
                "The operation is not available on the endpoint channel.");
    }
}
