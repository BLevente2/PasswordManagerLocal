using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Backend.Services;

/// <summary>
/// Owns synchronization-item coalescing, local model timestamps, tombstones, and deleted-user cleanup.
/// </summary>
public sealed class SyncItemLifecycleService : ISyncItemLifecycleService
{
    private readonly ISyncItemRepository _syncItems;
    private readonly ISyncQueueRepository _syncQueue;
    private readonly IUserRepository _users;
    private readonly IGroupRepository _groups;
    private readonly IDeviceRepository _devices;
    private readonly IUserDeviceRepository _userDevices;
    private readonly ISyncTombstoneRepository _tombstones;
    private readonly ILocalDeviceMatcherService _localDevices;

    public SyncItemLifecycleService(
        ISyncItemRepository syncItems,
        ISyncQueueRepository syncQueue,
        IUserRepository users,
        IGroupRepository groups,
        IDeviceRepository devices,
        IUserDeviceRepository userDevices,
        ISyncTombstoneRepository tombstones,
        ILocalDeviceMatcherService localDevices)
    {
        _syncItems = syncItems;
        _syncQueue = syncQueue;
        _users = users;
        _groups = groups;
        _devices = devices;
        _userDevices = userDevices;
        _tombstones = tombstones;
        _localDevices = localDevices;
    }

    public async Task<SyncItem> GetOrCreateAsync(SyncItem item, long changedAtTs, CancellationToken ct = default)
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

    public async Task TouchLocalStateAsync(SyncItem item, long changedAtTs, CancellationToken ct = default)
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
            var group = await _groups.GetByIdAsync(item.ModelId, ct);
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
            var device = await _devices.GetByIdWithUserDevicesAsync(item.ModelId, ct);
            if (device is null || _localDevices.IsLocalDevice(device))
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

    public async Task RemoveItemsForDeletedUserAsync(
        Guid deletedUserId,
        Guid protectedSyncItemId,
        CancellationToken ct = default)
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

    private async Task RemoveTombstoneAsync(SyncItem item, CancellationToken ct)
    {
        var tombstone = await _tombstones.GetAsync(item.ModelId, item.ModelType, ct);
        if (tombstone is not null)
            _tombstones.Delete(tombstone);
    }

    private async Task<bool> IsSyncItemOnlyForDeletedUserAsync(
        SyncItem item,
        Guid deletedUserId,
        CancellationToken ct)
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
            var userIds = await _groups.ListUserIdsAsync(item.ModelId, ct);
            return userIds.Contains(deletedUserId) && userIds.All(userId => userId == deletedUserId);
        }

        if (item.ModelType == SyncModelType.Device)
        {
            var links = await _userDevices.ListByDeviceAsync(item.ModelId, ct);
            return links.Any(link => link.UserId == deletedUserId) &&
                   links.Where(link => !link.IsDeleted).All(link => link.UserId == deletedUserId);
        }

        return false;
    }

    private static SyncChangeType MergeChangeType(SyncChangeType current, SyncChangeType incoming)
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
