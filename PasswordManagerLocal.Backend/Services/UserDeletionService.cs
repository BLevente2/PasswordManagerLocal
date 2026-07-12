using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Backend.Services;

/// <summary>
/// Coordinates user removal, optional deletion propagation, and synchronization-runtime refresh.
/// </summary>
public sealed class UserDeletionService : IUserDeletionService
{
    private readonly IUserRepository _users;
    private readonly IUserLookupService _lookup;
    private readonly ISyncChangeQueueService _syncQueue;
    private readonly ISyncRuntimeService _syncRuntime;
    private readonly IUnitOfWork _uow;

    public UserDeletionService(
        IUserRepository users,
        IUserLookupService lookup,
        ISyncChangeQueueService syncQueue,
        ISyncRuntimeService syncRuntime,
        IUnitOfWork uow)
    {
        _users = users;
        _lookup = lookup;
        _syncQueue = syncQueue;
        _syncRuntime = syncRuntime;
        _uow = uow;
    }

    public Task DeleteUserAsync(User user, CancellationToken ct = default) =>
        DeleteUserAsync(user, false, ct);

    public async Task DeleteUserAsync(User user, bool enqueueSync, CancellationToken ct = default)
    {
        if (enqueueSync)
        {
            await _syncQueue.EnqueueAsync(new SyncItem
            {
                ModelId = user.UId,
                ModelType = SyncModelType.User,
                ChangeType = SyncChangeType.Deleted
            }, ct);
        }

        user.ClearEncryptedPayloads();
        _users.Delete(user);
        await _uow.SaveChangesAsync(ct);
        await _syncRuntime.RefreshSyncEnabledAsync(ct);
    }

    public Task DeleteUserAsync(Guid uid, CancellationToken ct = default) =>
        DeleteUserAsync(uid, false, ct);

    public async Task DeleteUserAsync(Guid uid, bool enqueueSync, CancellationToken ct = default)
    {
        var user = await _lookup.GetAndVerifyUserByUidAsync(uid, ct);
        await DeleteUserAsync(user, enqueueSync, ct);
    }

    public Task DeleteUserByTokenAsync(Guid token, CancellationToken ct = default) =>
        DeleteUserByTokenAsync(token, false, ct);

    public async Task DeleteUserByTokenAsync(Guid token, bool enqueueSync, CancellationToken ct = default)
    {
        var user = await _lookup.GetAndVerifyUserAsync(token, ct);
        await DeleteUserAsync(user, enqueueSync, ct);
    }
}
