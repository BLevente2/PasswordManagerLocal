using PasswordManagerLocal.Windows.AgentConnection;
using PasswordManagerLocal.Windows.Ipc.Contracts;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class FakeWindowsAgentControlConnector : IWindowsAgentControlConnector
{
    private readonly Queue<IWindowsAgentRegisteredConnection?> _results = new();
    private readonly List<WindowsUiIpcIdentity> _identities = new();

    public int AttemptCount { get; private set; }
    public IReadOnlyList<WindowsUiIpcIdentity> Identities => _identities;

    public void Enqueue(IWindowsAgentRegisteredConnection? connection) =>
        _results.Enqueue(connection);

    public Task<IWindowsAgentRegisteredConnection?> TryConnectAndRegisterAsync(
        string pipeName,
        WindowsUiIpcIdentity identity,
        TimeSpan connectTimeout,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AttemptCount++;
        _identities.Add(identity);
        return Task.FromResult(_results.Count == 0 ? null : _results.Dequeue());
    }
}
