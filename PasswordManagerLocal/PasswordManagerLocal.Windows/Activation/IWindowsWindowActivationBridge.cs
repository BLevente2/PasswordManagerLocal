namespace PasswordManagerLocal.Windows.Activation;

public interface IWindowsWindowActivationBridge
{
    Task<bool> ActivateAsync(CancellationToken cancellationToken = default);
}
