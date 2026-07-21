using PasswordManagerLocal.Windows.Ipc.Coordination;
using System.Diagnostics;

namespace PasswordManagerLocal.Windows.AgentConnection;

public sealed class WindowsAgentLauncher : IWindowsAgentLauncher
{
    private readonly string _agentExecutablePath;

    public WindowsAgentLauncher(string executableDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableDirectory);
        _agentExecutablePath = Path.Combine(
            Path.GetFullPath(executableDirectory),
            WindowsExecutableNames.AgentExecutableFileName);
    }

    public Task<bool> LaunchAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_agentExecutablePath))
            return Task.FromResult(false);

        try
        {
            return Task.FromResult(Process.Start(new ProcessStartInfo
            {
                FileName = _agentExecutablePath,
                WorkingDirectory = Path.GetDirectoryName(_agentExecutablePath)!,
                UseShellExecute = true
            }) is not null);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
