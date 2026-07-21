using PasswordManagerLocal.Windows.Agent.Tray;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class FakeTrayIconController : ITrayIconController
{
    public event EventHandler? OpenRequested;
    public event EventHandler? ExitRequested;

    public int InitializeCount { get; private set; }
    public int DisposeCount { get; private set; }
    public bool ThrowOnInitialize { get; set; }
    public TaskCompletionSource? InitializationRelease { get; set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InitializeCount++;
        if (ThrowOnInitialize)
            throw new InvalidOperationException("tray failed");
        if (InitializationRelease is not null)
            await InitializationRelease.Task.WaitAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }

    public void RaiseOpen() => OpenRequested?.Invoke(this, EventArgs.Empty);
    public void RaiseExit() => ExitRequested?.Invoke(this, EventArgs.Empty);
}
