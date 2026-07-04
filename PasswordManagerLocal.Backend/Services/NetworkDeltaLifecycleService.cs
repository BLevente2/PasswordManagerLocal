using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync;
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

public sealed class NetworkDeltaLifecycleService : INetworkDeltaLifecycleService
{
    private readonly IUserRepository _users;
    private readonly IDeviceRepository _devices;
    private readonly IUserDeviceRepository _userDevices;
    private readonly ISyncTombstoneRepository _tombstones;
    private readonly ISyncQueueRepository _syncQueue;
    private readonly ISyncQueueService _syncQueueService;
    private readonly ISyncDeviceIdentityService _syncDeviceIdentities;
    private readonly IDeviceIdentityService _identity;
    private readonly ISyncAuthorizationService _authorization;
    private readonly ISyncRuntimeService _syncRuntime;
    private readonly IAuthService _auth;
    private readonly IUnitOfWork _uow;

    public NetworkDeltaLifecycleService(
        IUserRepository users,
        IDeviceRepository devices,
        IUserDeviceRepository userDevices,
        ISyncTombstoneRepository tombstones,
        ISyncQueueRepository syncQueue,
        ISyncQueueService syncQueueService,
        ISyncDeviceIdentityService syncDeviceIdentities,
        IDeviceIdentityService identity,
        ISyncAuthorizationService authorization,
        ISyncRuntimeService syncRuntime,
        IAuthService auth,
        IUnitOfWork uow)
    {
        _users = users;
        _devices = devices;
        _userDevices = userDevices;
        _tombstones = tombstones;
        _syncQueue = syncQueue;
        _syncQueueService = syncQueueService;
        _syncDeviceIdentities = syncDeviceIdentities;
        _identity = identity;
        _authorization = authorization;
        _syncRuntime = syncRuntime;
        _auth = auth;
        _uow = uow;
    }

    public async Task BeforeSaveAsync(
        SyncDeltaPayload payload,
        Device sourceDevice,
        bool applied,
        long ts,
        CancellationToken ct = default)
    {
        var deletesUserProfile = SyncPayloadRules.IsDeletedUserPayload(payload);

        if (!SyncPayloadRules.DeletesSourceDevice(payload, sourceDevice) &&
            !SyncPayloadRules.DeletesLocalUserProfile(payload, _identity) &&
            !deletesUserProfile)
        {
            await TouchSourceDeviceAsync(sourceDevice, ct);
        }

        if (applied && deletesUserProfile)
            await CleanupSourceDeviceAfterUserDeletionAsync(payload.ModelId, sourceDevice, ct);
    }

    public async Task AfterSaveAsync(
        SyncDeltaPayload payload,
        Device sourceDevice,
        bool applied,
        long ts,
        CancellationToken ct = default)
    {
        if (SyncPayloadRules.DeletesLocalUserProfile(payload, _identity) ||
            (payload.ModelType == SyncModelType.User && payload.ChangeType == SyncChangeType.Deleted))
        {
            await _syncRuntime.RefreshSyncEnabledAsync(ct);
        }

        if (applied)
            await RefreshAffectedSessionCachesAsync(payload, ct);

        if (applied && SyncPayloadRules.ShouldPropagate(payload, _identity))
            await PropagateIncomingDeltaAsync(payload, sourceDevice.Id, ts, ct);

        if (applied && SyncPayloadRules.IsRemoteUserDeviceDeletion(payload, _identity))
            await CleanupDetachedDeviceIfUserDeviceDeletionCompletedAsync(payload, ts, ct);

        if (applied &&
            payload.ModelType == SyncModelType.UserDevice &&
            payload.ChangeType != SyncChangeType.Deleted &&
            payload.UserDevice is { IsSyncOn: true, IsDeleted: false } enabledLink &&
            enabledLink.DeviceId != _identity.LocalDeviceId)
        {
            await _syncQueueService.EnqueueUserCatchUpAsync(enabledLink.UserId, enabledLink.DeviceId, ct);
        }
    }

    private async Task RefreshAffectedSessionCachesAsync(SyncDeltaPayload payload, CancellationToken ct)
    {
        if (payload.ModelType != SyncModelType.User)
            return;

        if (payload.ChangeType == SyncChangeType.Deleted)
        {
            _auth.LogoutUser(payload.ModelId, AuthSessionInvalidationReason.ProfileRemoved);
            return;
        }

        var user = await _users.GetByIdWithRelationsAsync(payload.ModelId, ct);
        if (user is null)
            return;

        await _auth.RefreshSyncedUserSessionsAsync(user, ct);
    }


    public async Task<bool> TryAcknowledgeAlreadyDeletedUserAsync(SyncDeltaPayload payload, Device sourceDevice, long ts, CancellationToken ct)
    {
        if (!SyncPayloadRules.IsDeletedUserPayload(payload))
            return false;

        var existing = await _users.GetByIdAsync(payload.ModelId, ct);
        if (existing is not null)
            return false;

        var tombstone = await _tombstones.GetAsync(payload.ModelId, SyncModelType.User, ct);
        if (tombstone is null)
            return false;

        if (ts > tombstone.DeletedAtTs)
            await _tombstones.UpsertAsync(payload.ModelId, SyncModelType.User, ts, ct);

        await CleanupSourceDeviceAfterUserDeletionAsync(payload.ModelId, sourceDevice, ct);
        return true;
    }


    private async Task CleanupSourceDeviceAfterUserDeletionAsync(Guid deletedUserId, Device sourceDevice, CancellationToken ct)
    {
        if (await _userDevices.HasAnyActiveLinkForDeviceExceptUserAsync(sourceDevice.Id, deletedUserId, ct))
        {
            await TouchSourceDeviceAsync(sourceDevice, ct);
            return;
        }

        _syncDeviceIdentities.TryRemove(sourceDevice);
        _devices.Delete(sourceDevice);
    }


    private async Task CleanupDetachedDeviceIfUserDeviceDeletionCompletedAsync(SyncDeltaPayload payload, long ts, CancellationToken ct)
    {
        if (payload.UserDevice is null || payload.UserDevice.DeviceId == _identity.LocalDeviceId)
            return;

        if (await _syncQueue.HasPendingForModelAsync(payload.ModelId, SyncModelType.UserDevice, ct))
            return;

        var deletedUserDevice = await _userDevices.GetByModelIdAsync(payload.ModelId, ct);
        if (deletedUserDevice is not null && !deletedUserDevice.IsDeleted)
            return;

        await _tombstones.UpsertAsync(payload.ModelId, SyncModelType.UserDevice, ts, ct);

        if (!await _userDevices.HasAnyActiveLinkForDeviceAsync(payload.UserDevice.DeviceId, ct))
        {
            var device = await _devices.GetByIdWithUserDevicesAsync(payload.UserDevice.DeviceId, ct);
            if (device is not null)
            {
                _syncDeviceIdentities.TryRemove(device);
                _devices.Delete(device);
            }
        }
        else if (deletedUserDevice is not null)
        {
            _userDevices.Delete(deletedUserDevice);
        }

        await _uow.SaveChangesAsync(ct);
    }


    public async Task TouchSourceDeviceAsync(Device sourceDevice, CancellationToken ct)
    {
        sourceDevice.LastSync = DateTime.UtcNow;
        sourceDevice.LastSeen = DateTime.UtcNow;
        sourceDevice.InvalidSyncAttemptCount = 0;
        sourceDevice.LastInvalidSyncAttemptAt = null;
        sourceDevice.GenerateIntegrityHash();

        await RefreshCachedDeviceAsync(sourceDevice, ct);
    }


    private async Task RefreshCachedDeviceAsync(Device device, CancellationToken ct)
    {
        _syncDeviceIdentities.TryRemove(device);

        if (!device.IsTrusted || device.IsBlocked)
            return;

        if (!await _authorization.HasEligibleUserForDeviceAsync(device.Id, ct))
            return;

        if (await _syncQueue.HasPendingForDeviceAsync(device.Id, ct))
            _syncDeviceIdentities.TryAdd(device);
    }


    private Task PropagateIncomingDeltaAsync(SyncDeltaPayload payload, Guid sourceDeviceId, long changedAtTs, CancellationToken ct) =>
        _syncQueueService.EnqueuePropagationAsync(new SyncItem
        {
            ModelId = payload.ModelId,
            ModelType = payload.ModelType,
            ChangeType = payload.ChangeType,
            ChangedAtTs = changedAtTs
        }, sourceDeviceId, changedAtTs, ct);
}
