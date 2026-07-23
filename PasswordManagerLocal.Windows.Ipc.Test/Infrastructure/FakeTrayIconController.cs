using PasswordManagerLocal.Windows.Agent.Tray;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class FakeTrayIconController : ITrayIconController
{
    public event EventHandler? OpenRequested;
    public event EventHandler? ExitRequested;

    public int InitializeCount { get; private set; }
    public int DisposeCount { get; private set; }
    public bool ThrowOnInitialize { get; set; }
    public Exception? DisposeFailure { get; set; }
    public TaskCompletionSource? InitializationRelease { get; set; }
    public ICollection<string>? OperationLog { get; set; }
    public int ExitFailureCount { get; private set; }
    public string? LastExitFailureMessage { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InitializeCount++;
        OperationLog?.Add("tray-start");
        if (ThrowOnInitialize)
            throw new InvalidOperationException("tray failed");
        if (InitializationRelease is not null)
            await InitializationRelease.Task.WaitAsync(cancellationToken);
    }

    public Task ShowExitFailureAsync(
        string safeMessage,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExitFailureCount++;
        LastExitFailureMessage = safeMessage;
        OperationLog?.Add("tray-exit-failure");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        OperationLog?.Add("tray-dispose");
        return DisposeFailure is null
            ? ValueTask.CompletedTask
            : ValueTask.FromException(DisposeFailure);
    }

    public void RaiseOpen() => OpenRequested?.Invoke(this, EventArgs.Empty);
    public void RaiseExit() => ExitRequested?.Invoke(this, EventArgs.Empty);
}
