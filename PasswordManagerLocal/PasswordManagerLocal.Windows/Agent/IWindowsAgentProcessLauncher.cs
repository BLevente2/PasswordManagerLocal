using System.Diagnostics;

namespace PasswordManagerLocal.Windows.AgentConnection;

public interface IWindowsAgentProcessLauncher
{
    bool TryStart(ProcessStartInfo startInfo);
}
