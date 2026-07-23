using PasswordManagerLocal.Windows.AgentConnection;
using PasswordManagerLocal.Windows.Ipc.Contracts;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class FakeWindowsAgentRegisteredConnection : IWindowsAgentRegisteredConnection
{
    private readonly TaskCompletionSource _completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsConnected { get; set; } = true;
    public int? AgentProcessId { get; set; } = 6000;
    public Task Completion => _completion.Task;
    public int DisposeCount { get; private set; }
    public BackendRuntimeStatusDto BackendStatus { get; set; } = new(
        BackendRuntimeStatusState.Ready,
        BackendRuntimeFailureStatusKind.None,
        Failure: null,
        RequiresProcessRestart: false,
        DateTimeOffset.UtcNow);
    public DatabaseResetResultDto DatabaseResetResult { get; set; } = new(
        Completed: true,
        RequiresProcessRestart: false,
        SafeMessage: null);

    public Task<BackendRuntimeStatusDto> GetBackendRuntimeStatusAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(BackendStatus);
    }

    public Task<DatabaseResetResultDto> ResetDatabaseAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(DatabaseResetResult);
    }

    public void Disconnect(Exception? failure = null)
    {
        IsConnected = false;
        if (failure is null)
            _completion.TrySetResult();
        else
            _completion.TrySetException(failure);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        IsConnected = false;
        _completion.TrySetResult();
        return ValueTask.CompletedTask;
    }
}
