using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Backend.Services;

public sealed class UserDeltaApplierService : IUserDeltaApplierService
{
    public UserDeltaApplierService(
        IUserRepository users,
        ISyncTombstoneRepository tombstones,
        ISyncChangeQueueService syncQueueService)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(tombstones);
        ArgumentNullException.ThrowIfNull(syncQueueService);
    }

    public Task<bool> ApplyAsync(SyncDeltaPayload delta, Guid sourceDeviceId, long ts, CancellationToken ct)
    {
        if (delta.ModelType == SyncModelType.User && delta.ChangeType == SyncChangeType.Deleted)
        {
            throw new InvalidDataException(
                "Generic timestamp-based user deletion is unsupported; receive a signed AccountDeletion control operation instead.");
        }

        throw new InvalidOperationException(
            "Ordinary user updates must be handled by the pending snapshot or signed control-operation inbox.");
    }
}
