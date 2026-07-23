using PasswordManagerLocal.Windows.Activation;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class FakeWindowsUiShutdownBridge : IWindowsUiShutdownBridge
{
    public bool Result { get; set; } = true;
    public int ShutdownCount { get; private set; }

    public Task<bool> ShutdownAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ShutdownCount++;
        return Task.FromResult(Result);
    }
}
