namespace PasswordManagerLocal.Windows.Agent.Hosting;

public interface IWindowsAgentHost : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
