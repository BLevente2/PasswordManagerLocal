namespace PasswordManagerLocal.Windows.Agent.Ui;

public interface IWindowsUiCloseService
{
    Task<bool> RequestCloseAsync(CancellationToken cancellationToken = default);
}
