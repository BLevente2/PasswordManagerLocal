using PasswordManagerLocal.Windows.Activation;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class FakeWindowsWindowActivationBridge : IWindowsWindowActivationBridge
{
    public bool Result { get; set; } = true;
    public int ActivationCount { get; private set; }

    public Task<bool> ActivateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ActivationCount++;
        return Task.FromResult(Result);
    }
}
