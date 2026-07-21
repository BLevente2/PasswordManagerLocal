using PasswordManagerLocal.Windows.Agent.Status;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class FakeWindowsBackgroundSyncSettingsReader : IWindowsBackgroundSyncSettingsReader
{
    public bool IsEnabled { get; set; }
    public bool ThrowOnRead { get; set; }

    public Task<bool> ReadIsEnabledAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ThrowOnRead)
            throw new IOException("settings failure");
        return Task.FromResult(IsEnabled);
    }
}
