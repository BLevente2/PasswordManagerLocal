using System.Data;
using Microsoft.EntityFrameworkCore;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Persistence;

namespace PasswordManagerLocalBackend.Repositories;

public sealed class SyncQueueRepository : ISyncQueueRepository
{
    private readonly DbSet<SyncQueueItem> _queue;

    public SyncQueueRepository(AppDbContext context)
    {
        _queue = context.SyncQueueItems;
    }


    public async Task<bool> ExistsAsync(Guid syncItemId, Guid deviceId, CancellationToken ct = default) =>
        await _queue.AnyAsync(x => x.SyncItemId == syncItemId && x.DeviceId == deviceId, ct);

    public async Task EnqueueAsync(Guid syncItemId, Guid deviceId, CancellationToken ct = default)
    {
        var newItem = new SyncQueueItem
        {
            SyncItemId = syncItemId,
            DeviceId = deviceId
        };

        await _queue.AddAsync(newItem);
    }

    public async Task<bool> TryEnqueueAsync(Guid syncItemId, Guid deviceId, CancellationToken ct = default)
    {
        try
        {
            await EnqueueAsync(syncItemId, deviceId, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task EnqueueAsync(IEnumerable<SyncQueueItem> items, CancellationToken ct = default) =>
        await _queue.AddRangeAsync(items, ct);
}