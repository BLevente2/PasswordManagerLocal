using PasswordManagerLocal.Runtime.Abstractions;
using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Backend.Hosting;
using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeBackendRuntime : IBackendRuntime
{
    private readonly IEndpoints _endpoints;
    private bool _interactiveSessionActive;

    public FakeBackendRuntime(IEndpoints endpoints)
    {
        _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
        Snapshot = new BackendRuntimeSnapshot(
            BackendRuntimeState.NotStarted,
            BackendRuntimeFailureKind.None,
            null,
            DateTimeOffset.UtcNow);
    }

    public BackendRuntimeSnapshot Snapshot { get; private set; }
    public InteractiveSessionLifecycleSnapshot InteractiveSessionSnapshot { get; private set; } = new(
        InteractiveSessionLifecycleState.None,
        null,
        DateTimeOffset.UtcNow);
    public SyncRuntimeSnapshot SyncSnapshot { get; private set; } = new(SyncRuntimeState.Disabled, null);
    public int EnsureStartedCalls { get; private set; }
    public int WaitUntilReadyCalls { get; private set; }
    public int OpenInteractiveSessionCalls { get; private set; }
    public int ResetCalls { get; private set; }
    public int StopCalls { get; private set; }
    public int ClosedInteractiveSessionCalls { get; private set; }
    public Exception? StartupFailure { get; set; }
    public Exception? InteractiveSessionFailure { get; set; }
    public Exception? InteractiveSessionDisposeFailure { get; set; }
    public Exception? StopFailure { get; set; }
    public Action? AfterStart { get; set; }

    public event EventHandler<BackendRuntimeStateChangedEventArgs>? StateChanged;
    public event EventHandler<SyncRuntimeStateChangedEventArgs>? SyncStateChanged;

    public Task EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureStartedCalls++;

        if (StartupFailure is not null)
            throw StartupFailure;

        SetSnapshot(new BackendRuntimeSnapshot(
            BackendRuntimeState.Ready,
            BackendRuntimeFailureKind.None,
            null,
            DateTimeOffset.UtcNow));
        InteractiveSessionSnapshot = new InteractiveSessionLifecycleSnapshot(
            InteractiveSessionLifecycleState.None,
            null,
            DateTimeOffset.UtcNow);
        AfterStart?.Invoke();
        return Task.CompletedTask;
    }

    public async Task WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        WaitUntilReadyCalls++;
        await EnsureStartedAsync(cancellationToken);
    }

    public async Task<IInteractiveBackendSession> OpenInteractiveSessionAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await WaitUntilReadyAsync(cancellationToken);

        if (_interactiveSessionActive)
            throw new InvalidOperationException("An interactive session is already active.");

        OpenInteractiveSessionCalls++;
        if (InteractiveSessionFailure is not null)
            throw InteractiveSessionFailure;

        _interactiveSessionActive = true;
        InteractiveSessionSnapshot = new InteractiveSessionLifecycleSnapshot(
            InteractiveSessionLifecycleState.Active,
            null,
            DateTimeOffset.UtcNow);
        return new FakeInteractiveBackendSession(
            _endpoints,
            () =>
            {
                _interactiveSessionActive = false;
                ClosedInteractiveSessionCalls++;
                InteractiveSessionSnapshot = InteractiveSessionDisposeFailure is null
                    ? new InteractiveSessionLifecycleSnapshot(
                        InteractiveSessionLifecycleState.None,
                        null,
                        DateTimeOffset.UtcNow)
                    : new InteractiveSessionLifecycleSnapshot(
                        InteractiveSessionLifecycleState.CleanupFailed,
                        InteractiveSessionDisposeFailure,
                        DateTimeOffset.UtcNow);
            },
            () => InteractiveSessionDisposeFailure);
    }

    public Task ResetDatabaseAndRestartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResetCalls++;
        SetSnapshot(new BackendRuntimeSnapshot(
            BackendRuntimeState.Ready,
            BackendRuntimeFailureKind.None,
            null,
            DateTimeOffset.UtcNow));
        InteractiveSessionSnapshot = new InteractiveSessionLifecycleSnapshot(
            InteractiveSessionLifecycleState.None,
            null,
            DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StopCalls++;
        if (StopFailure is not null)
            throw StopFailure;

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
