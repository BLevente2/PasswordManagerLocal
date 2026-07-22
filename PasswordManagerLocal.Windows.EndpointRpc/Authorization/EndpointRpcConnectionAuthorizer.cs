using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Lifecycle;
using PasswordManagerLocal.Windows.Ipc.Protocol;
using PasswordManagerLocal.Windows.Ipc.Server;

namespace PasswordManagerLocal.Windows.EndpointRpc.Authorization;

public sealed class EndpointRpcConnectionAuthorizer :
    IWindowsIpcHandshakeAuthorizer,
    IWindowsIpcConnectionLifecycleObserver
{
    private readonly IEndpointUiRegistrationResolver _registrationResolver;
    private readonly object _gate = new();
    private Guid? _activeConnectionId;

    public EndpointRpcConnectionAuthorizer(IEndpointUiRegistrationResolver registrationResolver) =>
        _registrationResolver = registrationResolver
            ?? throw new ArgumentNullException(nameof(registrationResolver));

    public IpcHandshakeAuthorizationDecision Authorize(IpcConnectionContext connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.PeerRole != IpcPeerRole.Ui)
        {
            return IpcHandshakeAuthorizationDecision.Reject(
                IpcErrorCode.UnexpectedPeerRole,
                "Only the registered UI may open the endpoint channel.");
        }

        if ((connection.PeerCapabilities & IpcCapabilities.EndpointRpc) == 0)
        {
            return IpcHandshakeAuthorizationDecision.Reject(
                IpcErrorCode.UnsupportedCapability,
                "The endpoint RPC capability is required.");
        }

        if (!_registrationResolver.IsRegistered(
                connection.PeerProcessId,
                connection.PeerSessionId))
        {
            return IpcHandshakeAuthorizationDecision.Reject(
                IpcErrorCode.UiNotRegistered,
                "The UI is not registered for endpoint access.");
        }

        lock (_gate)
        {
            if (_activeConnectionId.HasValue &&
                _activeConnectionId.Value != connection.ConnectionId)
            {
                return IpcHandshakeAuthorizationDecision.Reject(
                    IpcErrorCode.UiAlreadyRegistered,
                    "An endpoint connection is already active.");
            }

            _activeConnectionId = connection.ConnectionId;
            return IpcHandshakeAuthorizationDecision.Authorized;
        }
    }


    public bool IsAuthorizedConnection(IpcConnectionContext connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.PeerRole != IpcPeerRole.Ui ||
            (connection.PeerCapabilities & IpcCapabilities.EndpointRpc) == 0 ||
            !_registrationResolver.IsRegistered(connection.PeerProcessId, connection.PeerSessionId))
        {
            return false;
        }

        lock (_gate)
            return _activeConnectionId == connection.ConnectionId;
    }

    public ValueTask OnConnectionLifecycleChangedAsync(
        IpcConnectionLifecycleNotification notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (notification.State is not IpcConnectionLifecycleState.Disconnected and
            not IpcConnectionLifecycleState.Faulted)
        {
            return ValueTask.CompletedTask;
        }

        lock (_gate)
        {
            if (_activeConnectionId == notification.ConnectionId)
                _activeConnectionId = null;
        }

        return ValueTask.CompletedTask;
    }
}
