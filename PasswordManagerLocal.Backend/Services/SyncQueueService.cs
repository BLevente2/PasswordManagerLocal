using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using PasswordManagerLocal.Backend.Abstractions.Caching;

namespace PasswordManagerLocal.Backend.Services;

public sealed class SyncQueueService : ISyncQueueService
{
    private readonly ISyncQueueRepository _syncQueue;
    private readonly ISyncItemRepository _syncItems;
    private readonly IUserRepository _users;
    private readonly IGroupRepository _groups;
    private readonly IDeviceRepository _devices;
    private readonly IUserDeviceRepository _userDevices;
    private readonly ILocalUserDeviceRepository _localUserDevices;
    private readonly ISyncTombstoneRepository _tombstones;
    private readonly ISyncDeviceIdentityService _syncDeviceIdentities;
    private readonly IDiscoveredDeviceEndpointCache _endpointCache;
    private readonly IDeviceSyncTaskService _deviceSyncTasks;
    private readonly IDeviceIdentityService _identity;
    private readonly ISyncAuthorizationService _authorization;
    private readonly IUnitOfWork _uow;

    public SyncQueueService(
        ISyncQueueRepository syncQueue,
        ISyncItemRepository syncItems,
        IUserRepository users,
        IGroupRepository groups,
        IDeviceRepository devices,
        IUserDeviceRepository userDevices,
        ILocalUserDeviceRepository localUserDevices,
        ISyncTombstoneRepository tombstones,
        ISyncDeviceIdentityService syncDeviceIdentities,
        IDiscoveredDeviceEndpointCache endpointCache,
        IDeviceSyncTaskService deviceSyncTasks,
        IDeviceIdentityService identity,
        ISyncAuthorizationService authorization,
        IUnitOfWork uow)
    {
        _syncQueue = syncQueue;
        _syncItems = syncItems;
        _users = users;
        _groups = groups;
        _devices = devices;
        _userDevices = userDevices;
        _localUserDevices = localUserDevices;
        _tombstones = tombstones;
        _syncDeviceIdentities = syncDeviceIdentities;
        _endpointCache = endpointCache;
        _deviceSyncTasks = deviceSyncTasks;
        _identity = identity;
        _authorization = authorization;
        _uow = uow;
    }




    public Task EnqueueAsync(SyncItem item, CancellationToken ct = default) =>
        EnqueueCoreAsync(item, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), [], true, ct);


    public Task EnqueuePropagationAsync(SyncItem item, Guid sourceDeviceId, long changedAtTs, CancellationToken ct = default) =>
        EnqueueCoreAsync(item, changedAtTs, [sourceDeviceId], false, ct);


    private async Task EnqueueCoreAsync(
        SyncItem item,
        long changedAtTs,
        IReadOnlyCollection<Guid> excludedDeviceIds,
        bool touchLocalSyncState,
        CancellationToken ct)
    {
        item.ChangedAtTs = changedAtTs;

        var syncItem = await GetOrCreateSyncItemAsync(item, changedAtTs, ct);

        if (touchLocalSyncState)
            await TouchLocalSyncStateAsync(syncItem, changedAtTs, ct);

        var excludedDeviceIdSet = syncItem.ChangedAtTs > changedAtTs
            ? new HashSet<Guid>()
            : excludedDeviceIds
                .Where(id => id != Guid.Empty)
                .ToHashSet();

        var devices = await ListTargetDevicesAsync(syncItem, touchLocalSyncState, ct);
        var targetDevices = devices
            .Where(d => !excludedDeviceIdSet.Contains(d.Id) && !IsLocalDevice(d))
            .GroupBy(d => d.Id)
            .Select(g => g.First())
            .ToList();

        if (targetDevices.Count != 0)
        {
            var deviceIds = targetDevices
                .Select(d => d.Id)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            var queuedDeviceIds = await _syncQueue.ListQueuedDeviceIdsAsync(syncItem.Id, deviceIds, ct);
            var queuedDeviceIdSet = queuedDeviceIds.ToHashSet();

            var queueItems = deviceIds
                .Where(id => !queuedDeviceIdSet.Contains(id))
                .Select(id => new SyncQueueItem
                {
                    DeviceId = id,
                    SyncItemId = syncItem.Id
                })
                .ToList();

            if (queueItems.Count != 0)
                await _syncQueue.EnqueueAsync(queueItems, ct);
        }

        if (touchLocalSyncState &&
            syncItem.ModelType == SyncModelType.User &&
            syncItem.ChangeType == SyncChangeType.Deleted)
            await RemoveSyncItemsForDeletedUserAsync(syncItem.ModelId, syncItem.Id, ct);

        await _uow.SaveChangesAsync(ct);

        RefreshDiscoveryCache(targetDevices);
    }


    private async Task RemoveSyncItemsForDeletedUserAsync(Guid deletedUserId, Guid protectedSyncItemId, CancellationToken ct)
    {
        var syncItems = (await _syncItems.ListAllAsync(ct))
            .Where(syncItem => syncItem.Id != protectedSyncItemId)
            .DistinctBy(syncItem => syncItem.Id)
            .ToList();

        var syncItemIdsToDelete = new List<Guid>();
        foreach (var syncItem in syncItems)
        {
            if (await IsSyncItemOnlyForDeletedUserAsync(syncItem, deletedUserId, ct))
                syncItemIdsToDelete.Add(syncItem.Id);
        }

        foreach (var syncItemId in syncItemIdsToDelete)
        {
            var trackedSyncItem = await _syncItems.GetByIdAsync(syncItemId, ct);
            if (trackedSyncItem is not null)
                _syncItems.Delete(trackedSyncItem);
        }
    }


    private async Task<bool> IsSyncItemOnlyForDeletedUserAsync(SyncItem item, Guid deletedUserId, CancellationToken ct)
    {
        if (item.ModelType == SyncModelType.User)
            return item.ModelId == deletedUserId;

        if (item.ModelType == SyncModelType.UserDevice)
        {
            var link = await _userDevices.GetByModelIdAsync(item.ModelId, ct);
            return link?.UserId == deletedUserId;
        }

        if (item.ModelType == SyncModelType.Group)
        {
            var group = await _groups.GetByIdWithUsersAsync(item.ModelId, ct);
            return group is not null &&
                   group.Users.Any(user => user.UId == deletedUserId) &&
                   group.Users.All(user => user.UId == deletedUserId);
        }

        if (item.ModelType == SyncModelType.Device)
        {
            var links = await _userDevices.ListByDeviceAsync(item.ModelId, ct);
            return links.Any(link => link.UserId == deletedUserId) &&
                   links.Where(link => !link.IsDeleted).All(link => link.UserId == deletedUserId);
        }

        return false;
    }


    public async Task EnqueueForDeviceAsync(SyncItem item, Guid targetDeviceId, CancellationToken ct = default)
    {
        if (targetDeviceId == Guid.Empty || targetDeviceId == _identity.LocalDeviceId)
            return;

        var changedAtTs = item.ChangedAtTs > 0 ? item.ChangedAtTs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        item.ChangedAtTs = changedAtTs;
        var syncItem = await GetOrCreateSyncItemAsync(item, changedAtTs, ct);
        if (!await _authorization.CanSendAsync(syncItem, targetDeviceId, ct))
        {
            await _uow.SaveChangesAsync(ct);
            return;
        }

        var existing = await _syncQueue.ListQueuedDeviceIdsAsync(syncItem.Id, [targetDeviceId], ct);
        if (existing.Count == 0)
            await _syncQueue.EnqueueAsync([new SyncQueueItem { DeviceId = targetDeviceId, SyncItemId = syncItem.Id }], ct);
        await _uow.SaveChangesAsync(ct);

        var target = await _devices.GetByIdAsync(targetDeviceId, ct);
        if (target is not null)
            RefreshDiscoveryCache([target]);
    }


    public async Task EnqueueUserCatchUpAsync(Guid userId, Guid targetDeviceId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdWithRelationsAsync(userId, ct);
        if (user is null)
            return;

        await EnqueueForDeviceAsync(new SyncItem
        {
            ModelId = userId,
            ModelType = SyncModelType.User,
            ChangeType = SyncChangeType.Updated,
            ChangedAtTs = ToSyncTimestamp(user.LastModifiedAt)
        }, targetDeviceId, ct);

        foreach (var group in user.Groups)
        {
            await EnqueueForDeviceAsync(new SyncItem
            {
                ModelId = group.Id,
                ModelType = SyncModelType.Group,
                ChangeType = SyncChangeType.Updated,
                ChangedAtTs = ToSyncTimestamp(group.LastModifiedAt)
            }, targetDeviceId, ct);
        }

        foreach (var link in user.UserDevices)
        {
            if (!link.IsDeleted && link.DeviceId != targetDeviceId)
            {
                var sourceDevice = await _devices.GetByIdAsync(link.DeviceId, ct);
                if (sourceDevice is not null)
                {
                    await EnqueueForDeviceAsync(new SyncItem
                    {
                        ModelId = link.DeviceId,
                        ModelType = SyncModelType.Device,
                        ChangeType = SyncChangeType.Updated,
                        ChangedAtTs = ToSyncTimestamp(sourceDevice.LastModifiedAt)
                    }, targetDeviceId, ct);
                }
            }

            await EnqueueForDeviceAsync(new SyncItem
            {
                ModelId = SyncIdentityUtil.BuildUserDeviceModelId(link.UserId, link.DeviceId),
                ModelType = SyncModelType.UserDevice,
                ChangeType = link.IsDeleted ? SyncChangeType.Deleted : SyncChangeType.Updated,
                ChangedAtTs = ToSyncTimestamp(link.LastModifiedAt)
            }, targetDeviceId, ct);
        }
    }


    public async Task<bool> TryEnqueueAsync(SyncItem item, CancellationToken ct = default)
    {
        try
        {
            await EnqueueAsync(item, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }


    private async Task<SyncItem> GetOrCreateSyncItemAsync(SyncItem item, long changedAtTs, CancellationToken ct)
    {
        var existing = await _syncItems.GetAsync(item.ModelId, item.ModelType, ct);
        if (existing is not null)
        {
            if (existing.ChangedAtTs > changedAtTs)
                return existing;

            if (await _syncQueue.HasPendingForSyncItemAsync(existing.Id, ct))
                existing.ChangeType = MergeChangeType(existing.ChangeType, item.ChangeType);
            else
                existing.ChangeType = item.ChangeType;

            existing.ChangedAtTs = changedAtTs;
            _syncItems.Update(existing);
            return existing;
        }

        item.ChangedAtTs = changedAtTs;
        await _syncItems.AddAsync(item, ct);
        return item;
    }


    private async Task TouchLocalSyncStateAsync(SyncItem item, long changedAtTs, CancellationToken ct)
    {
        if (item.ChangeType == SyncChangeType.Deleted && item.ModelType != SyncModelType.UserDevice)
        {
            await _tombstones.UpsertAsync(item.ModelId, item.ModelType, changedAtTs, ct);
            return;
        }

        var modifiedAt = DateTimeOffset.FromUnixTimeMilliseconds(changedAtTs);

        if (item.ModelType == SyncModelType.User)
        {
            var user = await _users.GetByIdWithRelationsAsync(item.ModelId, ct);
            if (user is null)
                return;

            user.LastModifiedAt = modifiedAt;
            user.GenerateIntegrityHash();
            _users.Update(user);
            await RemoveTombstoneAsync(item, ct);
            return;
        }

        if (item.ModelType == SyncModelType.Group)
        {
            var group = await _groups.GetByIdWithUsersAsync(item.ModelId, ct);
            if (group is null)
                return;

            group.LastModifiedAt = modifiedAt;
            group.GenerateIntegrityHash();
            _groups.Update(group);
            await RemoveTombstoneAsync(item, ct);
            return;
        }

        if (item.ModelType == SyncModelType.Device)
        {
            var device = await _devices.GetByIdWithUsersAsync(item.ModelId, ct);
            if (device is null)
                return;

            if (IsLocalDevice(device))
                return;

            device.LastModifiedAt = modifiedAt;
            device.GenerateIntegrityHash();
            _devices.Update(device);
            await RemoveTombstoneAsync(item, ct);
            return;
        }

        if (item.ModelType == SyncModelType.UserDevice)
        {
            var userDevice = await _userDevices.GetByModelIdAsync(item.ModelId, ct);
            if (userDevice is null)
                return;

            userDevice.VerifyIntegrity();
            userDevice.LastModifiedAt = modifiedAt;
            userDevice.GenerateIntegrityHash();
            _userDevices.Update(userDevice);
        }
    }


    private async Task RemoveTombstoneAsync(SyncItem item, CancellationToken ct)
    {
        var tombstone = await _tombstones.GetAsync(item.ModelId, item.ModelType, ct);
        if (tombstone is not null)
            _tombstones.Delete(tombstone);
    }


    private async Task<IReadOnlyList<Device>> ListTargetDevicesAsync(SyncItem item, bool touchLocalSyncState, CancellationToken ct = default)
    {
        if (item.ModelType == SyncModelType.User)
            return await ListUserTargetDevicesAsync(item.ModelId, ct);

        if (item.ModelType == SyncModelType.Group)
            return await ListGroupTargetDevicesAsync(item.ModelId, ct);

        if (item.ModelType == SyncModelType.Device)
            return await ListDeviceTargetDevicesAsync(item.ModelId, ct);

        if (item.ModelType == SyncModelType.UserDevice)
        {
            var userDevice = await _userDevices.GetByModelIdAsync(item.ModelId, ct);
            if (userDevice is null)
                return [];

            return await ListUserDeviceChangeTargetDevicesAsync(
                userDevice.UserId,
                userDevice.DeviceId,
                touchLocalSyncState && item.ChangeType == SyncChangeType.Deleted,
                ct);
        }

        return [];
    }


    private async Task<IReadOnlyList<Device>> ListUserTargetDevicesAsync(Guid userId, CancellationToken ct)
    {
        if (!await _localUserDevices.IsSyncOnAsync(userId, ct))
            return [];

        var links = await _userDevices.ListByUserAsync(userId, ct);
        return SelectDistinctDevices(links.Where(link => !link.IsDeleted && link.IsSyncOn));
    }


    private async Task<IReadOnlyList<Device>> ListGroupTargetDevicesAsync(Guid groupId, CancellationToken ct)
    {
        var group = await _groups.GetByIdAsNoTrackingWithUsersAsync(groupId, ct);
        if (group is null || group.Users.Count == 0)
            return [];

        var locallyEnabledUserIds = (await _localUserDevices.ListSyncOnUserIdsAsync(ct)).ToHashSet();
        var enabledGroupUserIds = group.Users
            .Select(user => user.UId)
            .Where(locallyEnabledUserIds.Contains)
            .Distinct()
            .ToList();
        if (enabledGroupUserIds.Count == 0)
            return [];

        var links = await _userDevices.ListByUsersAsync(enabledGroupUserIds, ct);
        return SelectDistinctDevices(links.Where(link => !link.IsDeleted && link.IsSyncOn));
    }


    private async Task<IReadOnlyList<Device>> ListDeviceTargetDevicesAsync(Guid sourceDeviceId, CancellationToken ct)
    {
        var sourceLinks = await _userDevices.ListByDeviceAsync(sourceDeviceId, ct);
        var sourceUserIds = sourceLinks
            .Where(link => !link.IsDeleted)
            .Select(link => link.UserId)
            .Distinct()
            .ToHashSet();
        if (sourceUserIds.Count == 0)
            return [];

        var enabledUserIds = (await _localUserDevices.ListSyncOnUserIdsAsync(ct))
            .Where(sourceUserIds.Contains)
            .Distinct()
            .ToList();
        if (enabledUserIds.Count == 0)
            return [];

        var targetLinks = await _userDevices.ListByUsersAsync(enabledUserIds, ct);
        return SelectDistinctDevices(targetLinks.Where(link =>
            link.DeviceId != sourceDeviceId &&
            !link.IsDeleted &&
            link.IsSyncOn));
    }


    private async Task<IReadOnlyList<Device>> ListUserDeviceChangeTargetDevicesAsync(
        Guid userId,
        Guid changedDeviceId,
        bool includeChangedDevice,
        CancellationToken ct)
    {
        if (!await _localUserDevices.IsSyncOnAsync(userId, ct))
            return [];

        var links = await _userDevices.ListByUserAsync(userId, ct);
        return SelectDistinctDevices(links.Where(link =>
            link.DeviceId == changedDeviceId
                ? includeChangedDevice
                : !link.IsDeleted && link.IsSyncOn));
    }


    private IReadOnlyList<Device> SelectDistinctDevices(IEnumerable<UserDevice> links) =>
        links
            .Where(link => link.Device is not null)
            .Select(link => link.Device!)
            .DistinctBy(device => device.Id)
            .ToList();


    private void RefreshDiscoveryCache(IReadOnlyList<Device> targetDevices)
    {
        if (targetDevices.Count == 0)
            return;

        if (!_identity.IsSyncOn)
        {
            foreach (var device in targetDevices)
            {
                _syncDeviceIdentities.TryRemove(device);
                _endpointCache.TryRemove(device.TlsCertFingerprint);
            }

            return;
        }

        foreach (var device in targetDevices)
        {
            if (CanBeAddedToDiscoveryCache(device))
            {
                _syncDeviceIdentities.TryAdd(device);
                TryStartCachedEndpointSync(device);
            }
            else
            {
                _syncDeviceIdentities.TryRemove(device);
                _endpointCache.TryRemove(device.TlsCertFingerprint);
            }
        }
    }


    private void TryStartCachedEndpointSync(Device device)
    {
        if (!_identity.IsSyncOn)
            return;

        if (!_endpointCache.TryGetByFingerprint(device.TlsCertFingerprint, out var endpoint) || endpoint is null)
            return;

        _deviceSyncTasks.TryStart(endpoint, device);
    }


    private bool CanBeAddedToDiscoveryCache(Device device) =>
        device.IsTrusted &&
        !device.IsBlocked &&
        device.PublicKey.Length != 0 &&
        device.SignPublicKey.Length != 0 &&
        !string.IsNullOrWhiteSpace(device.TlsCertFingerprint) &&
        !IsLocalDevice(device);


    private bool IsLocalDevice(Device device)
    {
        if (!_identity.IsInitialized)
            return false;

        if (device.Id == _identity.LocalDeviceId)
            return true;

        if (device.SignPublicKey.SequenceEqual(_identity.SignPublicKey))
            return true;

        return string.Equals(FingerprintUtil.NormalizeOrEmpty(device.TlsCertFingerprint), FingerprintUtil.NormalizeOrEmpty(_identity.FingerprintHex), StringComparison.OrdinalIgnoreCase);
    }




    private long ToSyncTimestamp(DateTimeOffset modifiedAt) =>
        (modifiedAt == default ? DateTimeOffset.UtcNow : modifiedAt).ToUnixTimeMilliseconds();


    private SyncChangeType MergeChangeType(SyncChangeType current, SyncChangeType incoming)
    {
        if (current == incoming)
            return current;

        if (current == SyncChangeType.Created && incoming == SyncChangeType.Updated)
            return SyncChangeType.Created;

        if (incoming == SyncChangeType.Deleted)
            return SyncChangeType.Deleted;

        if (current == SyncChangeType.Deleted)
            return SyncChangeType.Deleted;

        return incoming;
    }
}
