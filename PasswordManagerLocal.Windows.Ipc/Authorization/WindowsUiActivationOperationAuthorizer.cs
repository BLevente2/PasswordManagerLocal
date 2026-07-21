using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Protocol;
using PasswordManagerLocal.Windows.Ipc.Server;

namespace PasswordManagerLocal.Windows.Ipc.Authorization;

public sealed class WindowsUiActivationOperationAuthorizer : IWindowsIpcOperationAuthorizer
{
    public IpcAuthorizationDecision Authorize(IpcRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Request.OperationId == IpcOperationId.RequestUiActivation
            ? IpcAuthorizationDecision.Allowed
            : IpcAuthorizationDecision.Denied(
                IpcErrorCode.UnauthorizedOperation,
                IpcErrorCategory.Validation,
                "Only UI activation is supported on this connection.");
    }
}
