using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface ISyncRuntimeService
{
    SyncRuntimeSnapshot Snapshot { get; }

    event EventHandler<SyncRuntimeStateChangedEventArgs>? StateChanged;

    Task RefreshSyncEnabledAsync(CancellationToken ct = default);
    Task BeginEnrollmentOnlyAsync(CancellationToken ct = default);
    Task EndEnrollmentOnlyAsync(CancellationToken ct = default);
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}
