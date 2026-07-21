using PasswordManagerLocal.Windows.Ipc.Protocol;

namespace PasswordManagerLocal.Windows.Ipc.Transport;

public interface IWindowsIpcConnection : IAsyncDisposable
{
    Guid ConnectionId { get; }
    bool IsConnected { get; }

    ValueTask<IpcFrame?> ReadFrameAsync(CancellationToken cancellationToken = default);
    ValueTask WriteFrameAsync(IpcFrame frame, CancellationToken cancellationToken = default);
}
