using System.Diagnostics;

namespace PasswordManagerLocal.Windows.AgentConnection;

public sealed class WindowsAgentProcessLauncher : IWindowsAgentProcessLauncher
{
    public bool TryStart(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        return Process.Start(startInfo) is not null;
    }
}
