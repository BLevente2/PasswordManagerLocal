using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Backend.Services;

public sealed class UserDeltaApplierService : IUserDeltaApplierService
{
    private readonly IUserRepository _users;
    private readonly ISyncTombstoneRepository _tombstones;
    private readonly ISyncChangeQueueService _syncQueueService;

    public UserDeltaApplierService(
        IUserRepository users,
        ISyncTombstoneRepository tombstones,
        ISyncChangeQueueService syncQueueService)
    {
        _users = users;
        _tombstones = tombstones;
        _syncQueueService = syncQueueService;
    }

    public async Task<bool> ApplyAsync(SyncDeltaPayload delta, Guid sourceDeviceId, long ts, CancellationToken ct)
    {
        var existing = await _users.GetByIdWithRelationsAsync(delta.ModelId, ct);

        if (delta.ChangeType == SyncChangeType.Deleted)
        {
            if (existing is not null && IsIncomingOlderOrSame(existing.LastModifiedAt, ts))
                return false;

            if (existing is not null)
                await PropagateDeletedUserBeforeLocalRemovalAsync(existing, sourceDeviceId, ts, ct);

            if (existing is not null)
                _users.Delete(existing);

            await _tombstones.UpsertAsync(delta.ModelId, delta.ModelType, ts, ct);
            return true;
        }

        throw new InvalidOperationException(
            "Ordinary user updates must be handled by the pending snapshot inbox and cannot replace canonical encrypted blobs.");
    }

    private Task PropagateDeletedUserBeforeLocalRemovalAsync(User user, Guid sourceDeviceId, long ts, CancellationToken ct) =>
        _syncQueueService.EnqueuePropagationAsync(new SyncItem
        {
            ModelId = user.UId,
            ModelType = SyncModelType.User,
            ChangeType = SyncChangeType.Deleted,
            ChangedAtTs = ts
        }, sourceDeviceId, ts, ct);

    private static bool IsIncomingOlderOrSame(DateTimeOffset local, long incomingTs) =>
        local.ToUnixTimeMilliseconds() >= incomingTs;
}
