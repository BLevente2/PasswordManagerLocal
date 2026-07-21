using PasswordManagerLocal.Windows.Ipc.Authorization;
using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Protocol;
using PasswordManagerLocal.Windows.Ipc.Server;

namespace PasswordManagerLocal.Windows.Agent.Hosting;

public sealed class WindowsAgentOperationAuthorizer : IWindowsIpcOperationAuthorizer
{
    private readonly IWindowsAgentStateSource _stateSource;
    private readonly IWindowsIpcOperationAuthorizer _innerAuthorizer;

    public WindowsAgentOperationAuthorizer(
        IWindowsAgentStateSource stateSource,
        IWindowsIpcOperationAuthorizer innerAuthorizer)
    {
        _stateSource = stateSource ?? throw new ArgumentNullException(nameof(stateSource));
        _innerAuthorizer = innerAuthorizer ?? throw new ArgumentNullException(nameof(innerAuthorizer));
    }

    public IpcAuthorizationDecision Authorize(IpcRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_stateSource.State is AgentState.Stopping or AgentState.Stopped &&
            context.Request.OperationId is not IpcOperationId.Ping and not IpcOperationId.GetAgentStatus)
        {
            return IpcAuthorizationDecision.Denied(
                IpcErrorCode.AgentStopping,
                IpcErrorCategory.Availability,
                "The Windows agent is stopping.",
                isRetryable: true);
        }

        return _innerAuthorizer.Authorize(context);
    }
}
