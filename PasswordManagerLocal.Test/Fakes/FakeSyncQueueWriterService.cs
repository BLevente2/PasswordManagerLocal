using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeSyncQueueWriterService : ISyncQueueWriterService
{
    public List<SyncItem> EnqueuedItems { get; } = [];

    public Task EnqueueAsync(
        SyncItem item,
        long changedAtTs,
        IReadOnlyCollection<Guid> excludedDeviceIds,
        bool touchLocalSyncState,
        bool activateTargets,
        CancellationToken ct = default)
    {
        item.ChangedAtTs = changedAtTs;
        EnqueuedItems.Add(item);
        return Task.CompletedTask;
    }

    public Task EnqueueForDeviceAsync(SyncItem item, Guid targetDeviceId, CancellationToken ct = default)
    {
        EnqueuedItems.Add(item);
        return Task.CompletedTask;
    }
}
