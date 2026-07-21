using PasswordManagerLocal.Windows.AgentConnection;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class FakeWindowsAgentRegisteredConnection : IWindowsAgentRegisteredConnection
{
    public bool IsConnected { get; set; } = true;
    public int DisposeCount { get; private set; }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        IsConnected = false;
        return ValueTask.CompletedTask;
    }
}
