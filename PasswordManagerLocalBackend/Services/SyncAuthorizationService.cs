using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Sync;

namespace PasswordManagerLocalBackend.Services;

public sealed class SyncAuthorizationService : ISyncAuthorizationService
{
    private readonly IGroupRepository _groups;
    private readonly IDeviceRepository _devices;
    private readonly IUserDeviceRepository _userDevices;
    private readonly ILocalUserDeviceRepository _localUsers;
    private readonly IDeviceIdentityService _identity;

    public SyncAuthorizationService(
        IGroupRepository groups,
        IDeviceRepository devices,
        IUserDeviceRepository userDevices,
        ILocalUserDeviceRepository localUsers,
        IDeviceIdentityService identity)
    {
        _groups = groups;
        _devices = devices;
        _userDevices = userDevices;
        _localUsers = localUsers;
        _identity = identity;
    }

    public async Task<bool> CanSendAsync(SyncItem item, Guid targetDeviceId, CancellationToken ct = default)
    {
        if (!_identity.IsSyncOn || targetDeviceId == Guid.Empty || targetDeviceId == _identity.LocalDeviceId)
            return false;

        if (item.ModelType == SyncModelType.User)
            return item.ChangeType == SyncChangeType.Deleted || await IsUserRouteEnabledAsync(item.ModelId, targetDeviceId, ct);

        if (item.ModelType == SyncModelType.UserDevice)
        {
            var link = await _userDevices.GetByModelIdAsync(item.ModelId, ct);
            if (link is null || !await _localUsers.IsSyncOnAsync(link.UserId, ct))
                return false;

            if (link.DeviceId == targetDeviceId)
                return item.ChangeType == SyncChangeType.Deleted && link.IsDeleted;

            return await IsUserRouteEnabledAsync(link.UserId, targetDeviceId, ct);
        }

        if (item.ModelType == SyncModelType.Group)
        {
            var group = await _groups.GetByIdWithUsersAsync(item.ModelId, ct);
            return group is not null && await AnyUserRouteEnabledAsync(group.Users.Select(u => u.UId), targetDeviceId, ct);
        }

        if (item.ModelType == SyncModelType.Device)
        {
            var device = await _devices.GetByIdWithUsersAsync(item.ModelId, ct);
            return device is not null && await AnyUserRouteEnabledAsync(device.UserDevices.Where(x => !x.IsDeleted).Select(x => x.UserId), targetDeviceId, ct);
        }

        return false;
    }

    public async Task<bool> CanReceiveAsync(SyncDeltaPayload payload, Guid sourceDeviceId, CancellationToken ct = default)
    {
        if (!_identity.IsSyncOn || sourceDeviceId == Guid.Empty || sourceDeviceId == _identity.LocalDeviceId)
            return false;

        if (payload.ModelType == SyncModelType.User)
            return await IsUserRouteEnabledAsync(payload.ModelId, sourceDeviceId, ct);

        if (payload.ModelType == SyncModelType.UserDevice)
        {
            if (payload.UserDevice is null || !await _localUsers.IsSyncOnAsync(payload.UserDevice.UserId, ct))
                return false;

            if (payload.UserDevice.DeviceId == _identity.LocalDeviceId)
                return payload.ChangeType == SyncChangeType.Deleted || payload.UserDevice.IsDeleted;

            return await IsUserRouteEnabledAsync(payload.UserDevice.UserId, sourceDeviceId, ct);
        }

        if (payload.ModelType == SyncModelType.Group)
        {
            var ids = payload.Group?.UserIds ?? [];
            if (ids.Count == 0)
            {
                var group = await _groups.GetByIdWithUsersAsync(payload.ModelId, ct);
                ids = group?.Users.Select(u => u.UId).ToList() ?? [];
            }
            return await AnyUserRouteEnabledAsync(ids, sourceDeviceId, ct);
        }

        if (payload.ModelType == SyncModelType.Device)
        {
            var ids = payload.Device?.UserIds ?? [];
            if (ids.Count == 0)
            {
                var device = await _devices.GetByIdWithUsersAsync(payload.ModelId, ct);
                ids = device?.UserDevices.Where(x => !x.IsDeleted).Select(x => x.UserId).ToList() ?? [];
            }
            return await AnyUserRouteEnabledAsync(ids, sourceDeviceId, ct);
        }

        return false;
    }

    public async Task<bool> HasEligibleUserForDeviceAsync(Guid deviceId, CancellationToken ct = default)
    {
        var links = await _userDevices.ListActiveByDeviceAsync(deviceId, ct);
        foreach (var link in links)
        {
            if (link.IsSyncOn && await _localUsers.IsSyncOnAsync(link.UserId, ct))
                return true;
        }
        return false;
    }

    private async Task<bool> IsUserRouteEnabledAsync(Guid userId, Guid remoteDeviceId, CancellationToken ct)
    {
        if (!await _localUsers.IsSyncOnAsync(userId, ct))
            return false;
        return await _userDevices.HasActiveLinkAsync(userId, remoteDeviceId, ct);
    }

    private async Task<bool> AnyUserRouteEnabledAsync(IEnumerable<Guid> userIds, Guid remoteDeviceId, CancellationToken ct)
    {
        foreach (var userId in userIds.Where(x => x != Guid.Empty).Distinct())
        {
            if (await IsUserRouteEnabledAsync(userId, remoteDeviceId, ct))
                return true;
        }
        return false;
    }
}
