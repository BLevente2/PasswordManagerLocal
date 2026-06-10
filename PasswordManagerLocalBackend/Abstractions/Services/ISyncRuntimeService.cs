namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface ISyncRuntimeService
{
    Task SetSyncEnabledAsync(bool isSyncOn, CancellationToken ct = default);
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}
