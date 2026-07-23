using PasswordManagerLocal.Runtime.Abstractions;

namespace PasswordManagerLocal.Windows.Settings;

public sealed class WindowsPhase6BackgroundSyncSettingsStore : IBackgroundSyncSettingsStore
{
    public Task<BackgroundSyncSettings> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new BackgroundSyncSettings(false));
    }

    public Task WriteAsync(
        BackgroundSyncSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException(
            "Windows background synchronization settings become operational in Phase 7.");
    }
}
