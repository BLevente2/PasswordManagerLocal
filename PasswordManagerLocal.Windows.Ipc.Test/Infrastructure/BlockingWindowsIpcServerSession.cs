using PasswordManagerLocal.Windows.Ipc.Server;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class BlockingWindowsIpcServerSession : IWindowsIpcServerSession
{
    private readonly TaskCompletionSource _release = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public int RunCount { get; private set; }
    public int DisposeCount { get; private set; }
    public bool FailRun { get; set; }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        RunCount++;
        if (FailRun)
            throw new IOException("session failed");

        using var registration = cancellationToken.Register(() => _release.TrySetResult());
        await _release.Task;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        _release.TrySetResult();
        return ValueTask.CompletedTask;
    }

    public void Complete() => _release.TrySetResult();
}
