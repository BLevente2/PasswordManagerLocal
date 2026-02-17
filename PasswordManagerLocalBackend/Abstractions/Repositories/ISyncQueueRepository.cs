using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Abstractions.Repositories;

public interface ISyncQueueRepository
{
    Task<bool> ExistsAsync(Guid syncItemId, Guid deviceId, CancellationToken ct = default);
    Task EnqueueAsync(Guid syncItemId, Guid deviceId, CancellationToken ct = default);
    Task<bool> TryEnqueueAsync(Guid syncItemId, Guid deviceId, CancellationToken ct = default);
    Task EnqueueAsync(IEnumerable<SyncQueueItem> items, CancellationToken ct = default);
}