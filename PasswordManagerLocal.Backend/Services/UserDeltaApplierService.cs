using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Security;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models.Projections;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Utils;
using static PasswordManagerLocal.Backend.Constants.DataLengthConstants;
using System.Security.Cryptography;
using System.Text.Json;
using static PasswordManagerLocal.Backend.Utils.DataCodec;

namespace PasswordManagerLocal.Backend.Services;

public sealed class UserDeltaApplierService : IUserDeltaApplierService
{
    private readonly IUserRepository _users;
    private readonly ISyncTombstoneRepository _tombstones;
    private readonly ISyncChangeQueueService _syncQueueService;
    private readonly IUserDataBundleSyncService _bundleSync;
    private readonly ISyncRelationshipReconciliationService _relationships;

    public UserDeltaApplierService(
        IUserRepository users,
        ISyncTombstoneRepository tombstones,
        ISyncChangeQueueService syncQueueService,
        IUserDataBundleSyncService bundleSync,
        ISyncRelationshipReconciliationService relationships)
    {
        _users = users;
        _tombstones = tombstones;
        _syncQueueService = syncQueueService;
        _bundleSync = bundleSync;
        _relationships = relationships;
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

        if (delta.User is null)
            throw new InvalidDataException("User sync payload is missing.");

        if (existing is not null && await _bundleSync.TryMergeAsync(existing, delta.User, ts, ct))
        {
            await RemoveTombstoneAsync(delta, ct);
            return true;
        }

        if (existing is not null && !IncomingUserPayloadDominatesExistingBlobs(delta.User, existing))
            return false;

        if (existing is not null && IsIncomingOlderOrSame(existing.LastModifiedAt, ts))
            return false;

        var user = existing ?? CreateUser(delta.User);
        CopyUserData(delta.User, user);
        user.LastModifiedAt = FromTimestamp(ts);

        if (existing is null)
            await _users.AddAsync(user, ct);

        await _relationships.SyncUserGroupsAsync(user, delta.User.GroupIds, ct);
        await _relationships.SyncUserDevicesAsync(user, delta.User.DeviceIds, user.LastModifiedAt, ct);
        user.GenerateIntegrityHash();
        await RemoveTombstoneAsync(delta, ct);
        return true;
    }


    private async Task PropagateDeletedUserBeforeLocalRemovalAsync(User user, Guid sourceDeviceId, long ts, CancellationToken ct)
    {
        await _syncQueueService.EnqueuePropagationAsync(new SyncItem
        {
            ModelId = user.UId,
            ModelType = SyncModelType.User,
            ChangeType = SyncChangeType.Deleted,
            ChangedAtTs = ts
        }, sourceDeviceId, ts, ct);
    }


    private bool IncomingUserPayloadDominatesExistingBlobs(UserSyncPayload incoming, User existing) =>
        incoming.GeneralUserDataLastModifiedAt >= existing.GeneralUserDataLastModifiedAt &&
        incoming.UserPasswordsDataLastModifiedAt >= existing.UserPasswordsDataLastModifiedAt &&
        incoming.UserDevicesDataLastModifiedAt >= existing.UserDevicesDataLastModifiedAt;


    private async Task RemoveTombstoneAsync(SyncDeltaPayload payload, CancellationToken ct)
    {
        var tombstone = await _tombstones.GetAsync(payload.ModelId, payload.ModelType, ct);
        if (tombstone is not null)
            _tombstones.Delete(tombstone);
    }


    private User CreateUser(UserSyncPayload payload) =>
        new()
        {
            UId = payload.UId
        };


    private void CopyUserData(UserSyncPayload source, User target)
    {
        target.UId = source.UId;
        target.UsernameHash = source.UsernameHash;
        target.UsernameSalt = source.UsernameSalt;
        target.PasswordSalt = source.PasswordSalt;
        target.EncryptedPayload = source.EncryptedPayload;
        target.EncryptedGeneralUserDataPayload = source.EncryptedGeneralUserDataPayload;
        target.EncryptedUserPasswordsDataPayload = source.EncryptedUserPasswordsDataPayload;
        target.EncryptedUserDevicesDataPayload = source.EncryptedUserDevicesDataPayload;
        target.UserDataLastModifiedAt = source.UserDataLastModifiedAt;
        target.GeneralUserDataLastModifiedAt = source.GeneralUserDataLastModifiedAt;
        target.UserPasswordsDataLastModifiedAt = source.UserPasswordsDataLastModifiedAt;
        target.UserDevicesDataLastModifiedAt = source.UserDevicesDataLastModifiedAt;
        target.IntegrityHash = source.IntegrityHash;
    }


    private DateTimeOffset FromTimestamp(long ts) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ts);


    private bool IsIncomingOlderOrSame(DateTimeOffset local, long incomingTs) =>
        local.ToUnixTimeMilliseconds() >= incomingTs;
}
