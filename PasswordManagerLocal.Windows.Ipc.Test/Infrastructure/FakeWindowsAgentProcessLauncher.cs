using PasswordManagerLocal.Windows.AgentConnection;
using System.Diagnostics;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class FakeWindowsAgentProcessLauncher : IWindowsAgentProcessLauncher
{
    public bool Result { get; set; } = true;
    public ProcessStartInfo? LastStartInfo { get; private set; }
    public int StartCount { get; private set; }

    public bool TryStart(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        StartCount++;
        LastStartInfo = startInfo;
        return Result;
    }
}
