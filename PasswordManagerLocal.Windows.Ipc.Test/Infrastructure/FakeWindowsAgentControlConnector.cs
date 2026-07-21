using PasswordManagerLocal.Windows.AgentConnection;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class FakeWindowsAgentControlConnector : IWindowsAgentControlConnector
{
    private readonly Queue<IWindowsAgentRegisteredConnection?> _results = new();

    public int AttemptCount { get; private set; }

    public void Enqueue(IWindowsAgentRegisteredConnection? connection) =>
        _results.Enqueue(connection);

    public Task<IWindowsAgentRegisteredConnection?> TryConnectAndRegisterAsync(
        string pipeName,
        TimeSpan connectTimeout,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AttemptCount++;
        return Task.FromResult(_results.Count == 0 ? null : _results.Dequeue());
    }
}
