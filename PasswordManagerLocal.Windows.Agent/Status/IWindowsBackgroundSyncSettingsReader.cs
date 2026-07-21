namespace PasswordManagerLocal.Windows.Agent.Status;

public interface IWindowsBackgroundSyncSettingsReader
{
    Task<bool> ReadIsEnabledAsync(CancellationToken cancellationToken = default);
}
