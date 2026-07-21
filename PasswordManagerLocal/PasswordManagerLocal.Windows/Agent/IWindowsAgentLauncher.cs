namespace PasswordManagerLocal.Windows.AgentConnection;

public interface IWindowsAgentLauncher
{
    Task<bool> LaunchAsync(CancellationToken cancellationToken = default);
}
