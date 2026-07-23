using PasswordManagerLocal.Backend.Hosting;
using PasswordManagerLocal.Runtime.Abstractions;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class FakeAgentBackendRuntimeLease : IBackendRuntimeLease
{
    private readonly ICollection<string>? _operationLog;
    private int _disposed;

    public FakeAgentBackendRuntimeLease(
        BackendLifetimeReason reason,
        ICollection<string>? operationLog = null)
    {
        Reason = reason;
        _operationLog = operationLog;
    }

    public BackendLifetimeReason Reason { get; }
    public int DisposeCount { get; private set; }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            DisposeCount++;
            _operationLog?.Add("lease-dispose");
        }
        return ValueTask.CompletedTask;
    }
}
