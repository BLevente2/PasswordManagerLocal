using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Abstractions.Repositories;

public interface ISyncQueueRepository
{
    Task<bool> ExistsAsync(Guid syncItemId, Guid deviceId, CancellationToken ct = default);
    Task<bool> HasPendingForDeviceAsync(Guid deviceId, CancellationToken ct = default);
    Task<bool> HasPendingForSyncItemAsync(Guid syncItemId, CancellationToken ct = default);
    Task<SyncQueueItem?> GetNextPendingForDeviceAsync(Guid deviceId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> ListQueuedDeviceIdsAsync(Guid syncItemId, IReadOnlyList<Guid> deviceIds, CancellationToken ct = default);
    Task EnqueueAsync(Guid syncItemId, Guid deviceId, CancellationToken ct = default);
    Task<bool> TryEnqueueAsync(Guid syncItemId, Guid deviceId, CancellationToken ct = default);
    Task EnqueueAsync(IEnumerable<SyncQueueItem> items, CancellationToken ct = default);
    void Update(SyncQueueItem item);
    void Delete(SyncQueueItem item);
}
