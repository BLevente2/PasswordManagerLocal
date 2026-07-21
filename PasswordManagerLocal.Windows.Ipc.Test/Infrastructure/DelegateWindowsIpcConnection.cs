using PasswordManagerLocal.Windows.Ipc.Protocol;
using PasswordManagerLocal.Windows.Ipc.Transport;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class DelegateWindowsIpcConnection : IWindowsIpcConnection
{
    private readonly Func<CancellationToken, ValueTask<IpcFrame?>> _read;
    private readonly Func<IpcFrame, CancellationToken, ValueTask> _write;
    private readonly Func<ValueTask> _dispose;
    private int _disposeStarted;

    public DelegateWindowsIpcConnection(
        Func<CancellationToken, ValueTask<IpcFrame?>> read,
        Func<IpcFrame, CancellationToken, ValueTask> write,
        Func<ValueTask> dispose)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _write = write ?? throw new ArgumentNullException(nameof(write));
        _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
        ConnectionId = Guid.NewGuid();
    }

    public Guid ConnectionId { get; }
    public bool IsConnected => Volatile.Read(ref _disposeStarted) == 0;

    public ValueTask<IpcFrame?> ReadFrameAsync(CancellationToken cancellationToken = default) =>
        _read(cancellationToken);

    public ValueTask WriteFrameAsync(
        IpcFrame frame,
        CancellationToken cancellationToken = default) =>
        _write(frame, cancellationToken);

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return ValueTask.CompletedTask;

        return _dispose();
    }
}
