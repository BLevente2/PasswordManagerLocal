using PasswordManagerLocal.Windows.AgentConnection;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class FakeWindowsAgentLauncher : IWindowsAgentLauncher
{
    public bool LaunchResult { get; set; } = true;
    public int LaunchCount { get; private set; }

    public Task<bool> LaunchAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LaunchCount++;
        return Task.FromResult(LaunchResult);
    }
}
