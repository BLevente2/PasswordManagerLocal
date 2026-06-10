using PasswordManagerLocalBackend.Abstractions.Persistence;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Services;

public sealed class SyncQueueService : ISyncQueueService
{
    private readonly ISyncQueueRepository _syncQueue;
    private readonly ISyncItemRepository _syncItems;
    private readonly IUserRepository _users;
    private readonly IGroupRepository _groups;
    private readonly IDeviceRepository _devices;
    private readonly IUserDeviceRepository _userDevices;
    private readonly ISyncTombstoneRepository _tombstones;
    private readonly ISyncDeviceIdentityService _syncDeviceIdentities;
    private readonly IDeviceIdentityService _identity;
    private readonly IUnitOfWork _uow;

    public SyncQueueService(
        ISyncQueueRepository syncQueue,
        ISyncItemRepository syncItems,
        IUserRepository users,
        IGroupRepository groups,
        IDeviceRepository devices,
        IUserDeviceRepository userDevices,
        ISyncTombstoneRepository tombstones,
        ISyncDeviceIdentityService syncDeviceIdentities,
        IDeviceIdentityService identity,
        IUnitOfWork uow)
    {
        _syncQueue = syncQueue;
        _syncItems = syncItems;
        _users = users;
        _groups = groups;
        _devices = devices;
        _userDevices = userDevices;
        _tombstones = tombstones;
        _syncDeviceIdentities = syncDeviceIdentities;
        _identity = identity;
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

        var devices = await ListTargetDevicesAsync(syncItem, ct);
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

        await _uow.SaveChangesAsync(ct);

        RefreshDiscoveryCache(targetDevices);
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
            {
                _syncDeviceIdentities.TryRemove(device);
                _devices.Delete(device);
                return;
            }

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

            userDevice.LastModifiedAt = modifiedAt;
            _userDevices.Update(userDevice);
        }
    }


    private async Task RemoveTombstoneAsync(SyncItem item, CancellationToken ct)
    {
        var tombstone = await _tombstones.GetAsync(item.ModelId, item.ModelType, ct);
        if (tombstone is not null)
            _tombstones.Delete(tombstone);
    }


    private async Task<IReadOnlyList<Device>> ListTargetDevicesAsync(SyncItem item, CancellationToken ct = default)
    {
        if (item.ModelType == SyncModelType.User)
            return await _devices.ListUserDevicesAsync(item.ModelId, ct);

        if (item.ModelType == SyncModelType.Group)
            return await _devices.ListGroupDevicesAsync(item.ModelId, ct);

        if (item.ModelType == SyncModelType.Device)
            return await _devices.ListDevicesLinkedToDeviceUsersAsync(item.ModelId, ct);

        if (item.ModelType == SyncModelType.UserDevice)
        {
            var userDevice = await _userDevices.GetByModelIdAsync(item.ModelId, ct);
            if (userDevice is null)
                return [];

            return await _devices.ListUserDeviceChangeTargetDevicesAsync(userDevice.UserId, userDevice.DeviceId, ct);
        }

        return [];
    }


    private void RefreshDiscoveryCache(IReadOnlyList<Device> targetDevices)
    {
        if (targetDevices.Count == 0)
            return;

        if (!_identity.IsSyncOn)
        {
            foreach (var device in targetDevices)
                _syncDeviceIdentities.TryRemove(device);

            return;
        }

        foreach (var device in targetDevices)
        {
            if (CanBeAddedToDiscoveryCache(device))
                _syncDeviceIdentities.TryAdd(device);
            else
                _syncDeviceIdentities.TryRemove(device);
        }
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

        return string.Equals(NormalizeFingerprint(device.TlsCertFingerprint), NormalizeFingerprint(_identity.FingerprintHex), StringComparison.OrdinalIgnoreCase);
    }


    private static string NormalizeFingerprint(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
            return string.Empty;

        return fingerprint.Replace(":", string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();
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
