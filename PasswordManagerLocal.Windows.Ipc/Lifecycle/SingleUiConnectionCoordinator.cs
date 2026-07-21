namespace PasswordManagerLocal.Windows.Ipc.Lifecycle;

public sealed class SingleUiConnectionCoordinator : IUiConnectionCoordinator
{
    private readonly object _gate = new();
    private Guid? _registeredConnectionId;

    public Guid? RegisteredConnectionId
    {
        get
        {
            lock (_gate)
                return _registeredConnectionId;
        }
    }

    public bool TryRegister(Guid connectionId)
    {
        if (connectionId == Guid.Empty)
            throw new ArgumentException("The connection ID cannot be empty.", nameof(connectionId));

        lock (_gate)
        {
            if (_registeredConnectionId is null)
            {
                _registeredConnectionId = connectionId;
                return true;
            }

            return _registeredConnectionId == connectionId;
        }
    }

    public bool Unregister(Guid connectionId)
    {
        if (connectionId == Guid.Empty)
            throw new ArgumentException("The connection ID cannot be empty.", nameof(connectionId));

        lock (_gate)
        {
            if (_registeredConnectionId != connectionId)
                return false;

            _registeredConnectionId = null;
            return true;
        }
    }
}
