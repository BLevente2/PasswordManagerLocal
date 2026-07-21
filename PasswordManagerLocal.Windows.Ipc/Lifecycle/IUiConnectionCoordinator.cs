namespace PasswordManagerLocal.Windows.Ipc.Lifecycle;

public interface IUiConnectionCoordinator
{
    Guid? RegisteredConnectionId { get; }

    bool TryRegister(Guid connectionId);
    bool Unregister(Guid connectionId);
}
