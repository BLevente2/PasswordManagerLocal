namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface ISyncRuntimeService
{
    Task RefreshSyncEnabledAsync(CancellationToken ct = default);
    Task BeginEnrollmentOnlyAsync(CancellationToken ct = default);
    Task EndEnrollmentOnlyAsync(CancellationToken ct = default);
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}
