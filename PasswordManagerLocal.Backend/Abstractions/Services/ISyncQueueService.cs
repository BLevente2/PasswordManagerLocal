using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface ISyncQueueService
{
    Task EnqueueAsync(SyncItem item, CancellationToken ct = default);
    Task EnqueuePropagationAsync(SyncItem item, Guid sourceDeviceId, long changedAtTs, CancellationToken ct = default);
    Task<bool> TryEnqueueAsync(SyncItem item, CancellationToken ct = default);
    Task EnqueueForDeviceAsync(SyncItem item, Guid targetDeviceId, CancellationToken ct = default);
    Task EnqueueUserCatchUpAsync(Guid userId, Guid targetDeviceId, CancellationToken ct = default);
}
