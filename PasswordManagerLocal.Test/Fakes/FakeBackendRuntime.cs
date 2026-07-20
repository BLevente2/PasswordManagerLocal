using PasswordManagerLocal.Runtime.Abstractions;
using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Backend.Hosting;
using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeBackendRuntime : IBackendRuntime
{
    private readonly IEndpoints _endpoints;

    public FakeBackendRuntime(IEndpoints endpoints)
    {
        _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
        Snapshot = new BackendRuntimeSnapshot(
            BackendRuntimeState.Ready,
            BackendRuntimeFailureKind.None,
            null,
            DateTimeOffset.UtcNow);
    }

    public BackendRuntimeSnapshot Snapshot { get; private set; }
    public SyncRuntimeSnapshot SyncSnapshot { get; private set; } = new(SyncRuntimeState.Disabled, null);
    public int GetEndpointsCalls { get; private set; }
    public int ResetCalls { get; private set; }

    public event EventHandler<BackendRuntimeStateChangedEventArgs>? StateChanged;
    public event EventHandler<SyncRuntimeStateChangedEventArgs>? SyncStateChanged;

    public Task EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<IEndpoints> GetEndpointsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GetEndpointsCalls++;
        return Task.FromResult(_endpoints);
    }

    public Task ResetDatabaseAndRestartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResetCalls++;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetSnapshot(new BackendRuntimeSnapshot(
            BackendRuntimeState.Stopped,
            BackendRuntimeFailureKind.None,
            null,
            DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void SetSnapshot(BackendRuntimeSnapshot snapshot)
    {
        var previous = Snapshot;
        Snapshot = snapshot;
        StateChanged?.Invoke(this, new BackendRuntimeStateChangedEventArgs(previous, snapshot));
    }

    public void SetSyncSnapshot(SyncRuntimeSnapshot snapshot)
    {
        var previous = SyncSnapshot;
        SyncSnapshot = snapshot;
        SyncStateChanged?.Invoke(this, new SyncRuntimeStateChangedEventArgs(previous, snapshot));
    }
}
