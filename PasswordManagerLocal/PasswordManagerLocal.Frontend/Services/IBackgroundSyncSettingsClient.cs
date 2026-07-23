namespace PasswordManagerLocal.Frontend.Services;

public interface IBackgroundSyncSettingsClient
{
    Task<BackgroundSyncClientState> GetStateAsync(
        CancellationToken cancellationToken = default);
    Task<BackgroundSyncChangeResult> SetEnabledAsync(
        bool isEnabled,
        CancellationToken cancellationToken = default);
}
