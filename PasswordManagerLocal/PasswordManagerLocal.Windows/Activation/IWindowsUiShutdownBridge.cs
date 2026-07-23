namespace PasswordManagerLocal.Windows.Activation;

public interface IWindowsUiShutdownBridge
{
    Task<bool> ShutdownAsync(CancellationToken cancellationToken = default);
}
