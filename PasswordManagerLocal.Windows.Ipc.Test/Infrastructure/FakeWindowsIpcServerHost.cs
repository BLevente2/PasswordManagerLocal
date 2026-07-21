using PasswordManagerLocal.Windows.Ipc.Server;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class FakeWindowsIpcServerHost : IWindowsIpcServerHost
{
    private readonly TaskCompletionSource _completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public int ActiveSessionCount { get; set; }
    public Exception? ListenerFailure { get; private set; }
    public Task Completion => _completion.Task;
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public int DisposeCount { get; private set; }
    public bool ThrowOnStart { get; set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartCount++;
        if (ThrowOnStart)
            throw new IOException("listener start failed");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StopCount++;
        _completion.TrySetResult();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        _completion.TrySetResult();
        return ValueTask.CompletedTask;
    }

    public void FailListener(Exception exception)
    {
        ListenerFailure = exception;
        _completion.TrySetException(exception);
    }
}
