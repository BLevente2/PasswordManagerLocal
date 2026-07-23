using PasswordManagerLocal.Windows.EndpointRpc.Client;
using PasswordManagerLocal.Windows.Ipc.Contracts;

namespace PasswordManagerLocal.Windows.EndpointRpc.Test.Infrastructure;

public sealed class FakeEndpointRpcAgentConnection : IEndpointRpcAgentConnection
{
    private readonly Queue<bool> _ensureResults = new();
    private TaskCompletionSource _completion = CreateCompletion();
    private bool _disposed;

    public bool IsConnected { get; private set; } = true;
    public long ConnectionGeneration { get; private set; } = 1;
    public Task Completion => _completion.Task;
    public int EnsureCount { get; private set; }
    public int ResetCount { get; private set; }
    public int DisposeCount { get; private set; }
    public bool DisconnectOnReset { get; set; }
    public BackendRuntimeStatusDto BackendStatus { get; set; } = new(
        BackendRuntimeStatusState.Ready,
        BackendRuntimeFailureStatusKind.None,
        null,
        false,
        DateTimeOffset.UtcNow);
    public DatabaseResetResultDto ResetResult { get; set; } = new(true, false, null);

    public void EnqueueEnsureResult(bool result) => _ensureResults.Enqueue(result);

    public Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCount++;
        var result = _ensureResults.Count == 0 ? true : _ensureResults.Dequeue();
        if (result && !IsConnected)
        {
            IsConnected = true;
            ConnectionGeneration++;
            _completion = CreateCompletion();
        }
        return Task.FromResult(result);
    }

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
        ResetCount++;
        if (DisconnectOnReset)
            Disconnect();
        return Task.FromResult(ResetResult);
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
        _disposed = true;
        IsConnected = false;
        _completion.TrySetResult();
        return ValueTask.CompletedTask;
    }

    private static TaskCompletionSource CreateCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
