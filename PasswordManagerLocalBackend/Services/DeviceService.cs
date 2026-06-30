using PasswordManagerLocalBackend.Abstractions.Persistence;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Responses;
using PasswordManagerLocalBackend.Sync;
using PasswordManagerLocalBackend.Utils;
using static PasswordManagerLocalBackend.Utils.DataValidationUtil;

namespace PasswordManagerLocalBackend.Services;

public sealed class DeviceService : IDeviceService
{
    private readonly IUserService _users;
    private readonly IAuthService _auth;
    private readonly IDeviceIdentityService _identity;
    private readonly IDeviceRepository _devices;
    private readonly IGroupRepository _groups;
    private readonly IUserDeviceRepository _userDevices;
    private readonly ILocalUserDeviceRepository _localUserDevices;
    private readonly ISyncQueueService _syncQueue;
    private readonly ISyncQueueRepository _syncQueueItems;
    private readonly ISyncDeviceIdentityService _syncDeviceIdentities;
    private readonly ISyncRuntimeService _syncRuntime;
    private readonly IUnitOfWork _uow;

    public DeviceService(
        IUserService users,
        IAuthService auth,
        IDeviceIdentityService identity,
        IDeviceRepository devices,
        IGroupRepository groups,
        IUserDeviceRepository userDevices,
        ILocalUserDeviceRepository localUserDevices,
        ISyncQueueService syncQueue,
        ISyncQueueRepository syncQueueItems,
        ISyncDeviceIdentityService syncDeviceIdentities,
        ISyncRuntimeService syncRuntime,
        IUnitOfWork uow)
    {
        _users = users;
        _auth = auth;
        _identity = identity;
        _devices = devices;
        _groups = groups;
        _userDevices = userDevices;
        _localUserDevices = localUserDevices;
        _syncQueue = syncQueue;
        _syncQueueItems = syncQueueItems;
        _syncDeviceIdentities = syncDeviceIdentities;
        _syncRuntime = syncRuntime;
        _uow = uow;
    }

    public Task<LocalDeviceInfoResponse> GetLocalDeviceInfoAsync(CancellationToken ct = default) =>
        Task.FromResult(new LocalDeviceInfoResponse
        {
            DeviceId = _identity.LocalDeviceId,
            TlsCertFingerprint = _identity.FingerprintHex,
            DeviceType = _identity.DeviceType,
            IsSyncOn = _identity.IsSyncOn,
            CreatedAt = _identity.CreatedAt
        });

    public async Task<bool> GetLocalUserSyncOnAsync(Guid token, CancellationToken ct = default)
    {
        var user = await _users.GetAndVerifyUserAsync(token, ct);
        var link = await EnsureLocalUserDeviceAsync(user.UId, ct);
        return link.IsSyncOn;
    }

    public async Task SetLocalUserSyncOnAsync(Guid token, bool isSyncOn, CancellationToken ct = default)
    {
        var user = await _users.GetAndVerifyUserAsync(token, ct);
        var link = await EnsureLocalUserDeviceAsync(user.UId, ct);
        if (link.IsSyncOn == isSyncOn)
            return;

        link.IsSyncOn = isSyncOn;
        link.GenerateIntegrityHash();
        _localUserDevices.Update(link);
        await _uow.SaveChangesAsync(ct);
        await _syncRuntime.RefreshSyncEnabledAsync(ct);

        if (isSyncOn)
        {
            var remotes = await _userDevices.ListByUserAsync(user.UId, ct);
            foreach (var deleted in remotes.Where(x => x.IsDeleted))
            {
                await _syncQueue.EnqueueForDeviceAsync(new SyncItem
                {
                    ModelId = SyncIdentityUtil.BuildUserDeviceModelId(deleted.UserId, deleted.DeviceId),
                    ModelType = SyncModelType.UserDevice,
                    ChangeType = SyncChangeType.Deleted,
                    ChangedAtTs = deleted.LastModifiedAt.ToUnixTimeMilliseconds()
                }, deleted.DeviceId, ct);
            }

            foreach (var remote in remotes.Where(x => !x.IsDeleted && x.IsSyncOn))
                await _syncQueue.EnqueueUserCatchUpAsync(user.UId, remote.DeviceId, ct);
        }
    }

    public Task SetLocalDeviceNameAsync(Guid token, string name, CancellationToken ct = default) =>
        SetEncryptedDeviceNameAsync(token, _identity.LocalDeviceId, name, ct);

    public async Task<IReadOnlyList<UserDeviceInfoResponse>> GetUserDevicesAsync(Guid token, CancellationToken ct = default)
    {
        var user = await _users.GetAndVerifyUserAsync(token, ct);
        var bundle = await _users.GetLoadAndVerifyUserDataBundleAsync(token, ct, user);
        var userDevicesData = bundle.UserDevicesData;
        var localLink = await EnsureLocalUserDeviceAsync(user.UId, ct);
        var links = await _userDevices.ListByUserAsync(user.UId, ct);

        var changed = EnsureEncryptedDeviceData(userDevicesData, _identity.LocalDeviceId, DateTimeOffset.UtcNow);
        foreach (var link in links.Where(x => !x.IsDeleted))
            changed |= EnsureEncryptedDeviceData(userDevicesData, link.DeviceId, link.LastModifiedAt);
        if (changed)
            await PersistUserDeviceDataAsync(bundle, token, ct);

        var encryptedDevices = userDevicesData.Devices.ToDictionary(d => d.Id);
        var result = new List<UserDeviceInfoResponse>();
        if (encryptedDevices.TryGetValue(_identity.LocalDeviceId, out var localDeviceData))
            result.Add(BuildLocalResponse(localLink, localDeviceData));

        foreach (var link in links.Where(x => !x.IsDeleted && x.Device is not null))
        {
            if (encryptedDevices.TryGetValue(link.DeviceId, out var deviceData))
                result.Add(BuildRemoteResponse(link, link.Device!, deviceData));
        }

        return result.OrderByDescending(d => d.IsCurrentDevice).ThenByDescending(d => d.LastSeen).ToList();
    }

    public Task SetUserDeviceNameAsync(Guid token, Guid deviceId, string name, CancellationToken ct = default) =>
        SetEncryptedDeviceNameAsync(token, deviceId, name, ct);

    public async Task SetUserDeviceSyncOnAsync(Guid token, Guid deviceId, bool isSyncOn, CancellationToken ct = default)
    {
        if (deviceId == _identity.LocalDeviceId)
        {
            await SetLocalUserSyncOnAsync(token, isSyncOn, ct);
            return;
        }

        var user = await _users.GetAndVerifyUserAsync(token, ct);
        var userDevice = await GetActiveRemoteUserDeviceAsync(user.UId, deviceId, ct);
        if (userDevice.IsSyncOn == isSyncOn)
        {
            if (!isSyncOn)
                await RemoveCachedDeviceIfNoPendingAsync(userDevice, ct);
            return;
        }

        userDevice.IsSyncOn = isSyncOn;
        userDevice.LastModifiedAt = DateTimeOffset.UtcNow;
        userDevice.GenerateIntegrityHash();
        _userDevices.Update(userDevice);
        await EnqueueUserDeviceChangeAsync(userDevice, SyncChangeType.Updated, ct);

        if (isSyncOn)
            await _syncQueue.EnqueueUserCatchUpAsync(user.UId, deviceId, ct);
        else
            await RemoveCachedDeviceIfNoPendingAsync(userDevice, ct);
    }

    public async Task UnblockUserDeviceAsync(Guid token, Guid deviceId, CancellationToken ct = default)
    {
        var user = await _users.GetAndVerifyUserAsync(token, ct);
        await GetActiveRemoteUserDeviceAsync(user.UId, deviceId, ct);
        var device = await _devices.GetByIdWithUserDevicesAsync(deviceId, ct) ?? throw new InvalidInputException();
        if (!device.IsBlocked && device.InvalidSyncAttemptCount == 0 && device.BlockedReason is null)
            return;

        device.IsBlocked = false;
        device.BlockedReason = null;
        device.BlockedAt = null;
        device.InvalidSyncAttemptCount = 0;
        device.LastInvalidSyncAttemptAt = null;
        device.LastModifiedAt = DateTimeOffset.UtcNow;
        device.GenerateIntegrityHash();
        _devices.Update(device);
        await _syncQueue.EnqueueAsync(new SyncItem { ModelId = device.Id, ModelType = SyncModelType.Device, ChangeType = SyncChangeType.Updated }, ct);
    }

    public async Task DisconnectUserDeviceAsync(Guid token, Guid deviceId, byte[] masterPassword, CancellationToken ct = default)
    {
        if (!IsValidPassword(masterPassword) || deviceId == _identity.LocalDeviceId)
            throw new InvalidInputException();
        var user = await _users.GetAndVerifyUserAsync(token, ct);
        if (!_auth.IsPasswordValid(token, masterPassword, user.PasswordSalt))
            throw new InvalidInputException();

        var userDevice = await GetActiveRemoteUserDeviceAsync(user.UId, deviceId, ct);
        var bundle = await _users.GetLoadAndVerifyUserDataBundleAsync(token, ct, user);
        var userDevicesData = bundle.UserDevicesData;
        var now = DateTimeOffset.UtcNow;
        userDevice.IsDeleted = true;
        userDevice.IsSyncOn = false;
        userDevice.DeletedAt = now;
        userDevice.LastModifiedAt = now;
        userDevice.GenerateIntegrityHash();
        _userDevices.Update(userDevice);

        var encryptedDevice = userDevicesData.Devices.FirstOrDefault(d => d.Id == deviceId);
        var userDeviceDataChanged = false;
        if (encryptedDevice is not null)
        {
            AddOrUpdateDeletedDeviceData(userDevicesData, encryptedDevice.Id, now);
            encryptedDevice.Dispose();
            userDevicesData.Devices.Remove(encryptedDevice);
            userDeviceDataChanged = true;
        }

        if (userDeviceDataChanged)
            await PersistUserDeviceDataAsync(bundle, token, ct, false);
        else
            await _uow.SaveChangesAsync(ct);

        await RemovePendingSyncsForUserToDeviceAsync(user.UId, deviceId, ct);

        if (userDeviceDataChanged)
        {
            await _syncQueue.EnqueueAsync(new SyncItem
            {
                ModelId = user.UId,
                ModelType = SyncModelType.User,
                ChangeType = SyncChangeType.Updated
            }, ct);
        }

        await EnqueueUserDeviceChangeAsync(userDevice, SyncChangeType.Deleted, ct);
    }

    private async Task SetEncryptedDeviceNameAsync(Guid token, Guid deviceId, string name, CancellationToken ct)
    {
        var normalizedName = NormalizeUserDeviceName(name);
        var user = await _users.GetAndVerifyUserAsync(token, ct);
        var bundle = await _users.GetLoadAndVerifyUserDataBundleAsync(token, ct, user);
        var userDevicesData = bundle.UserDevicesData;
        await EnsureLocalUserDeviceAsync(user.UId, ct);
        UserDevice? remoteLink = null;
        if (deviceId != _identity.LocalDeviceId)
            remoteLink = await GetActiveRemoteUserDeviceAsync(user.UId, deviceId, ct);

        if (userDevicesData.Devices.Any(d => d.Id != deviceId && string.Equals(d.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidInputException();

        var encryptedDevice = userDevicesData.Devices.FirstOrDefault(d => d.Id == deviceId);
        if (encryptedDevice is null)
        {
            encryptedDevice = new UserDeviceData
            {
                Id = deviceId,
                Name = normalizedName,
                LinkedAt = remoteLink?.LastModifiedAt ?? DateTimeOffset.UtcNow,
                LastUpdatedAt = DateTimeOffset.UtcNow
            };
            userDevicesData.DeletedDevices.RemoveAll(deleted => deleted.Id == encryptedDevice.Id);
            userDevicesData.Devices.Add(encryptedDevice);
        }
        else if (string.Equals(encryptedDevice.Name, normalizedName, StringComparison.Ordinal))
            return;
        else
        {
            encryptedDevice.Name = normalizedName;
            encryptedDevice.LastUpdatedAt = DateTimeOffset.UtcNow;
        }
        await PersistUserDeviceDataAsync(bundle, token, ct);
    }

    private async Task<LocalUserDevice> EnsureLocalUserDeviceAsync(Guid userId, CancellationToken ct)
    {
        var link = await _localUserDevices.GetAsync(userId, ct);
        if (link is not null)
            return link;
        link = new LocalUserDevice
        {
            UserId = userId,
            LocalDeviceIdentityId = _identity.LocalDeviceId,
            IsSyncOn = true
        };
        link.GenerateIntegrityHash();
        await _localUserDevices.AddAsync(link, ct);
        await _uow.SaveChangesAsync(ct);
        await _syncRuntime.RefreshSyncEnabledAsync(ct);
        return link;
    }

    private bool EnsureEncryptedDeviceData(UserDevicesData userDevicesData, Guid deviceId, DateTimeOffset linkedAt)
    {
        if (userDevicesData.Devices.Any(d => d.Id == deviceId))
            return false;

        var baseName = DeviceNameUtil.BuildDefaultDeviceName(deviceId);
        var deviceData = new UserDeviceData
        {
            Id = deviceId,
            Name = BuildUniqueEncryptedDeviceName(userDevicesData, baseName, deviceId),
            LinkedAt = linkedAt == default ? DateTimeOffset.UtcNow : linkedAt,
            LastUpdatedAt = DateTimeOffset.UtcNow
        };
        deviceData.GenerateIntegrityHash();
        userDevicesData.DeletedDevices.RemoveAll(deleted => deleted.Id == deviceData.Id);
        userDevicesData.Devices.Add(deviceData);
        return true;
    }

    private static void AddOrUpdateDeletedDeviceData(UserDevicesData userDevicesData, Guid deviceId, DateTimeOffset deletedAt)
    {
        var tombstone = userDevicesData.DeletedDevices.FirstOrDefault(deleted => deleted.Id == deviceId);
        if (tombstone is null)
        {
            tombstone = new DeletedUserDeviceData { Id = deviceId };
            userDevicesData.DeletedDevices.Add(tombstone);
        }

        if (deletedAt > tombstone.DeletedAt)
            tombstone.DeletedAt = deletedAt;

        tombstone.GenerateIntegrityHash();
    }


    private string BuildUniqueEncryptedDeviceName(UserDevicesData userDevicesData, string requestedName, Guid deviceId)
    {
        var baseName = string.IsNullOrWhiteSpace(requestedName) ? DeviceNameUtil.BuildDefaultDeviceName(deviceId) : requestedName.Trim();
        if (!IsEncryptedNameTaken(userDevicesData, baseName, deviceId)) return baseName;
        for (var i = 2; i < 100; i++)
        {
            var suffix = $"-{i}";
            var candidate = baseName[..Math.Min(baseName.Length, 64 - suffix.Length)] + suffix;
            if (!IsEncryptedNameTaken(userDevicesData, candidate, deviceId)) return candidate;
        }
        throw new InvalidInputException();
    }

    private bool IsEncryptedNameTaken(UserDevicesData userDevicesData, string name, Guid exceptDeviceId) =>
        userDevicesData.Devices.Any(d => d.Id != exceptDeviceId && string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

    private async Task PersistUserDeviceDataAsync(UserDataBundle bundle, Guid token, CancellationToken ct, bool enqueueSync = true)
    {
        foreach (var device in bundle.UserDevicesData.Devices) device.GenerateIntegrityHash();
        foreach (var deleted in bundle.UserDevicesData.DeletedDevices) deleted.GenerateIntegrityHash();
        bundle.UserDevicesData.GenerateIntegrityHash();
        await _users.UpdateUserDataBundleAsync(bundle, token, UserDataBlobKind.Devices, enqueueSync, ct);
    }

    private UserDeviceInfoResponse BuildLocalResponse(LocalUserDevice link, UserDeviceData deviceData) => new()
    {
        DeviceId = _identity.LocalDeviceId,
        Name = deviceData.Name,
        DeviceType = _identity.DeviceType,
        TlsCertFingerprint = _identity.FingerprintHex,
        LastSync = _identity.CreatedAt.UtcDateTime,
        LastSeen = DateTime.UtcNow,
        IsTrusted = true,
        IsBlocked = false,
        InvalidSyncAttemptCount = 0,
        IsSyncOn = link.IsSyncOn,
        IsDeleted = false,
        LinkedAt = deviceData.LinkedAt,
        DeletedAt = null,
        IsCurrentDevice = true
    };

    private UserDeviceInfoResponse BuildRemoteResponse(UserDevice link, Device device, UserDeviceData deviceData) => new()
    {
        DeviceId = link.DeviceId,
        Name = deviceData.Name,
        DeviceType = device.DeviceType,
        TlsCertFingerprint = device.TlsCertFingerprint,
        LastSync = device.LastSync,
        LastSeen = device.LastSeen,
        IsTrusted = device.IsTrusted,
        IsBlocked = device.IsBlocked,
        BlockedReason = device.BlockedReason,
        BlockedAt = device.BlockedAt,
        InvalidSyncAttemptCount = device.InvalidSyncAttemptCount,
        IsSyncOn = link.IsSyncOn,
        IsDeleted = link.IsDeleted,
        LinkedAt = deviceData.LinkedAt,
        DeletedAt = link.DeletedAt,
        IsCurrentDevice = false
    };

    private Task EnqueueUserDeviceChangeAsync(UserDevice userDevice, SyncChangeType changeType, CancellationToken ct) =>
        _syncQueue.EnqueueAsync(new SyncItem
        {
            ModelId = SyncIdentityUtil.BuildUserDeviceModelId(userDevice.UserId, userDevice.DeviceId),
            ModelType = SyncModelType.UserDevice,
            ChangeType = changeType
        }, ct);

    private async Task RemovePendingSyncsForUserToDeviceAsync(Guid userId, Guid targetDeviceId, CancellationToken ct)
    {
        var pendingItems = await _syncQueueItems.ListPendingForDeviceWithItemsAsync(targetDeviceId, ct);
        foreach (var queueItem in pendingItems)
        {
            if (queueItem.SyncItem is not null && await IsSyncItemOnlyForRemovedUserOrRouteAsync(queueItem.SyncItem, userId, targetDeviceId, ct))
                _syncQueueItems.Delete(queueItem);
        }
    }

    private async Task<bool> IsSyncItemOnlyForRemovedUserOrRouteAsync(SyncItem item, Guid removedUserId, Guid targetDeviceId, CancellationToken ct)
    {
        if (item.ModelType == SyncModelType.User)
            return item.ModelId == removedUserId;

        if (item.ModelType == SyncModelType.UserDevice)
        {
            var link = await _userDevices.GetByModelIdAsync(item.ModelId, ct);
            return link?.UserId == removedUserId;
        }

        if (item.ModelType == SyncModelType.Group)
        {
            var group = await _groups.GetByIdWithUsersAsync(item.ModelId, ct);
            if (group is null || group.Users.All(user => user.UId != removedUserId))
                return false;

            return !await AnyOtherUserCanStillSyncToTargetAsync(
                group.Users.Select(user => user.UId),
                removedUserId,
                targetDeviceId,
                ct);
        }

        if (item.ModelType == SyncModelType.Device)
        {
            var links = await _userDevices.ListByDeviceAsync(item.ModelId, ct);
            if (links.All(link => link.UserId != removedUserId))
                return false;

            return !await AnyOtherUserCanStillSyncToTargetAsync(
                links.Where(link => !link.IsDeleted && link.IsSyncOn).Select(link => link.UserId),
                removedUserId,
                targetDeviceId,
                ct);
        }

        return false;
    }

    private async Task<bool> AnyOtherUserCanStillSyncToTargetAsync(IEnumerable<Guid> userIds, Guid removedUserId, Guid targetDeviceId, CancellationToken ct)
    {
        foreach (var otherUserId in userIds.Where(id => id != Guid.Empty && id != removedUserId).Distinct())
        {
            if (await _localUserDevices.IsSyncOnAsync(otherUserId, ct) &&
                await _userDevices.HasActiveLinkAsync(otherUserId, targetDeviceId, ct))
                return true;
        }

        return false;
    }

    private async Task RemoveCachedDeviceIfNoPendingAsync(UserDevice userDevice, CancellationToken ct)
    {
        if (userDevice.Device is null || await _syncQueueItems.HasPendingForDeviceAsync(userDevice.DeviceId, ct))
            return;

        var links = await _userDevices.ListActiveByDeviceAsync(userDevice.DeviceId, ct);
        foreach (var link in links)
        {
            if (link.IsSyncOn && await _localUserDevices.IsSyncOnAsync(link.UserId, ct))
                return;
        }

        _syncDeviceIdentities.TryRemove(userDevice.Device);
    }

    private string NormalizeUserDeviceName(string name)
    {
        if (!IsValidUserDeviceName(name)) throw new InvalidInputException();
        return name.Trim();
    }

    private async Task<UserDevice> GetActiveRemoteUserDeviceAsync(Guid userId, Guid deviceId, CancellationToken ct)
    {
        if (deviceId == Guid.Empty || deviceId == _identity.LocalDeviceId) throw new InvalidInputException();
        var userDevice = await _userDevices.GetAsync(userId, deviceId, ct);
        if (userDevice is null || userDevice.IsDeleted || userDevice.Device is null) throw new InvalidInputException();
        return userDevice;
    }
}
