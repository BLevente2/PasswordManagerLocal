using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Lifecycle;
using PasswordManagerLocal.Windows.Ipc.Protocol;
using PasswordManagerLocal.Windows.Ipc.Server;

namespace PasswordManagerLocal.Windows.Agent.Hosting;

public sealed class WindowsAgentAdmissionRequestHandler : IWindowsIpcRequestHandler
{
    private readonly IWindowsAgentAdmissionGate _admissionGate;
    private readonly IWindowsIpcRequestHandler _inner;

    public WindowsAgentAdmissionRequestHandler(
        IWindowsAgentAdmissionGate admissionGate,
        IWindowsIpcRequestHandler inner)
    {
        _admissionGate = admissionGate ?? throw new ArgumentNullException(nameof(admissionGate));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public IpcOperationId OperationId => _inner.OperationId;

    public Task<IpcResponseEnvelope> HandleAsync(
        IpcRequestContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_admissionGate.TryEnter(out var admission))
        {
            return Task.FromResult(IpcResponseEnvelope.Failure(
                context.Request.CorrelationId,
                new IpcError(
                    IpcErrorCode.AgentUnavailable,
                    IpcErrorCategory.Availability,
                    "The agent is not accepting this operation.",
                    context.Request.CorrelationId,
                    DateTimeOffset.UtcNow,
                    IsRetryable: true,
                    RequiresProcessRestart: false)));
        }

        admission!.Dispose();
        return _inner.HandleAsync(context, cancellationToken);
    }
}
