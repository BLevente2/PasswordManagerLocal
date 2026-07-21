using PasswordManagerLocal.Windows.Ipc.Contracts;

namespace PasswordManagerLocal.Windows.Ipc.Client;

internal sealed class PendingIpcRequest
{
    private readonly TaskCompletionSource<IpcResponseEnvelope> _completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action _releaseCapacity;

    public PendingIpcRequest(Action releaseCapacity)
    {
        _releaseCapacity = releaseCapacity
            ?? throw new ArgumentNullException(nameof(releaseCapacity));
    }

    public Task<IpcResponseEnvelope> Task => _completion.Task;
    public bool IsCompleted => _completion.Task.IsCompleted;

    public bool TrySetResult(IpcResponseEnvelope response) =>
        Complete(() => _completion.TrySetResult(response));

    public bool TrySetException(Exception exception) =>
        Complete(() => _completion.TrySetException(exception));

    public bool TrySetCanceled(CancellationToken cancellationToken) =>
        Complete(() => _completion.TrySetCanceled(cancellationToken));

    private bool Complete(Func<bool> complete)
    {
        if (!complete())
            return false;

        _releaseCapacity();
        return true;
    }
}
