using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface ISyncQueueService
{
    Task EnqueueAsync(SyncItem item, CancellationToken ct = default);
    Task EnqueuePropagationAsync(SyncItem item, Guid sourceDeviceId, long changedAtTs, CancellationToken ct = default);
    Task<bool> TryEnqueueAsync(SyncItem item, CancellationToken ct = default);
}
