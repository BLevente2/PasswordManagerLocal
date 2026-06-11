using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalTest.Fakes;

public sealed class FakeSyncQueueService : ISyncQueueService
{
    public List<SyncItem> EnqueuedItems { get; } = [];

    public Task EnqueueAsync(SyncItem item, CancellationToken ct = default)
    {
        EnqueuedItems.Add(item);
        return Task.CompletedTask;
    }

    public Task EnqueuePropagationAsync(SyncItem item, Guid sourceDeviceId, long changedAtTs, CancellationToken ct = default)
    {
        item.ChangedAtTs = changedAtTs;
        EnqueuedItems.Add(item);
        return Task.CompletedTask;
    }

    public Task<bool> TryEnqueueAsync(SyncItem item, CancellationToken ct = default)
    {
        EnqueuedItems.Add(item);
        return Task.FromResult(true);
    }
}
