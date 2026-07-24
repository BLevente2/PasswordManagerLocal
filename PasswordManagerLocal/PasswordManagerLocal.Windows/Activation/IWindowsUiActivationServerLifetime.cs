namespace PasswordManagerLocal.Windows.Activation;

public interface IWindowsUiActivationServerLifetime : IAsyncDisposable
{
    void RequestStop();
}
