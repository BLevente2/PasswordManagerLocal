using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeSyncQueueService : ISyncQueueService
{
    public List<SyncItem> EnqueuedItems { get; } = [];
    public List<(SyncItem Item, Guid TargetDeviceId)> EnqueuedForDevices { get; } = [];
    public List<(Guid UserId, Guid TargetDeviceId)> UserCatchUpRequests { get; } = [];

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

    public Task EnqueueForDeviceAsync(SyncItem item, Guid targetDeviceId, CancellationToken ct = default)
    {
        EnqueuedItems.Add(item);
        EnqueuedForDevices.Add((item, targetDeviceId));
        return Task.CompletedTask;
    }

    public Task EnqueueUserCatchUpAsync(Guid userId, Guid targetDeviceId, CancellationToken ct = default)
    {
        UserCatchUpRequests.Add((userId, targetDeviceId));
        return Task.CompletedTask;
    }
}
