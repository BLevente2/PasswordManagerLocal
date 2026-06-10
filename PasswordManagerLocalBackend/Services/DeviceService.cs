using PasswordManagerLocalBackend.Abstractions.Persistence;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Responses;
using PasswordManagerLocalBackend.Sync;
using static PasswordManagerLocalBackend.Utils.DataValidationUtil;

namespace PasswordManagerLocalBackend.Services;

public sealed class DeviceService : IDeviceService
{
    private readonly IUserService _users;
    private readonly IAuthService _auth;
    private readonly IDeviceIdentityService _identity;
    private readonly IDeviceRepository _devices;
    private readonly IUserDeviceRepository _userDevices;
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
        IUserDeviceRepository userDevices,
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
        _userDevices = userDevices;
        _syncQueue = syncQueue;
        _syncQueueItems = syncQueueItems;
        _syncDeviceIdentities = syncDeviceIdentities;
        _syncRuntime = syncRuntime;
        _uow = uow;
    }





    public Task<bool> GetLocalDeviceSyncEnabledAsync(CancellationToken ct = default) =>
        Task.FromResult(_identity.IsSyncOn);


    public Task SetLocalDeviceSyncEnabledAsync(bool isSyncOn, CancellationToken ct = default) =>
        _syncRuntime.SetSyncEnabledAsync(isSyncOn, ct);




    public async Task<IReadOnlyList<UserDeviceInfoResponse>> GetUserDevicesAsync(Guid token, CancellationToken ct = default)
    {
        var user = await _users.GetAndVerifyUserAsync(token, ct);
        var currentDeviceId = await GetCurrentDeviceIdAsync(ct);
        var userDevices = await _userDevices.ListByUserAsync(user.UId, ct);

        return userDevices
            .Where(ud => !ud.IsDeleted && ud.Device is not null)
            .Select(ud => UserDeviceInfoResponse.FromUserDevice(ud, currentDeviceId))
            .OrderByDescending(d => d.IsCurrentDevice)
            .ThenByDescending(d => d.LastSeen)
            .ToList();
    }




    public async Task SetUserDeviceNameAsync(Guid token, Guid deviceId, string name, CancellationToken ct = default)
    {
        var normalizedName = NormalizeUserDeviceName(name);
        var user = await _users.GetAndVerifyUserAsync(token, ct);
        var userDevice = await GetActiveUserDeviceAsync(user.UId, deviceId, ct);

        if (string.Equals(userDevice.Name, normalizedName, StringComparison.Ordinal))
            return;

        if (await _userDevices.IsNameTakenAsync(user.UId, normalizedName, deviceId, ct))
            throw new InvalidInputException();

        userDevice.Name = normalizedName;
        userDevice.LastModifiedAt = DateTimeOffset.UtcNow;
        _userDevices.Update(userDevice);

        await _syncQueue.EnqueueAsync(new SyncItem
        {
            ModelId = SyncIdentityUtil.BuildUserDeviceModelId(userDevice.UserId, userDevice.DeviceId),
            ModelType = SyncModelType.UserDevice,
            ChangeType = SyncChangeType.Updated
        }, ct);
    }


    public async Task SetUserDeviceSyncEnabledAsync(Guid token, Guid deviceId, bool isSyncEnabled, CancellationToken ct = default)
    {
        var user = await _users.GetAndVerifyUserAsync(token, ct);
        var userDevice = await GetActiveUserDeviceAsync(user.UId, deviceId, ct);

        if (userDevice.IsSyncEnabled == isSyncEnabled)
        {
            if (!isSyncEnabled)
                await RemoveCachedDeviceIfNoPendingAsync(userDevice, ct);

            return;
        }

        userDevice.IsSyncEnabled = isSyncEnabled;
        userDevice.LastModifiedAt = DateTimeOffset.UtcNow;
        _userDevices.Update(userDevice);

        await _syncQueue.EnqueueAsync(new SyncItem
        {
            ModelId = SyncIdentityUtil.BuildUserDeviceModelId(userDevice.UserId, userDevice.DeviceId),
            ModelType = SyncModelType.UserDevice,
            ChangeType = SyncChangeType.Updated
        }, ct);
    }


    public async Task UnblockUserDeviceAsync(Guid token, Guid deviceId, CancellationToken ct = default)
    {
        var user = await _users.GetAndVerifyUserAsync(token, ct);
        await GetActiveUserDeviceAsync(user.UId, deviceId, ct);

        var device = await _devices.GetByIdWithUserDevicesAsync(deviceId, ct);
        if (device is null)
            throw new InvalidInputException();

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

        await _syncQueue.EnqueueAsync(new SyncItem
        {
            ModelId = device.Id,
            ModelType = SyncModelType.Device,
            ChangeType = SyncChangeType.Updated
        }, ct);
    }


    public async Task DisconnectUserDeviceAsync(Guid token, Guid deviceId, byte[] masterPassword, CancellationToken ct = default)
    {
        if (!IsValidPassword(masterPassword))
            throw new InvalidInputException();

        var user = await _users.GetAndVerifyUserAsync(token, ct);
        if (!_auth.IsPasswordValid(token, masterPassword, user.PasswordSalt))
            throw new InvalidInputException();

        var userDevice = await GetActiveUserDeviceAsync(user.UId, deviceId, ct);
        var now = DateTimeOffset.UtcNow;

        userDevice.IsDeleted = true;
        userDevice.IsSyncEnabled = false;
        userDevice.DeletedAt = now;
        userDevice.LastModifiedAt = now;
        _userDevices.Update(userDevice);

        await _syncQueue.EnqueueAsync(new SyncItem
        {
            ModelId = SyncIdentityUtil.BuildUserDeviceModelId(userDevice.UserId, userDevice.DeviceId),
            ModelType = SyncModelType.UserDevice,
            ChangeType = SyncChangeType.Deleted
        }, ct);
    }






    private async Task RemoveCachedDeviceIfNoPendingAsync(UserDevice userDevice, CancellationToken ct)
    {
        if (userDevice.Device is null)
            return;

        if (await _syncQueueItems.HasPendingForDeviceAsync(userDevice.DeviceId, ct))
            return;

        _syncDeviceIdentities.TryRemove(userDevice.Device);
    }


    private static string NormalizeUserDeviceName(string name)
    {
        if (!IsValidUserDeviceName(name))
            throw new InvalidInputException();

        return name.Trim();
    }


    private async Task<UserDevice> GetActiveUserDeviceAsync(Guid userId, Guid deviceId, CancellationToken ct)
    {
        if (deviceId == Guid.Empty)
            throw new InvalidInputException();

        var userDevice = await _userDevices.GetAsync(userId, deviceId, ct);
        if (userDevice is null || userDevice.IsDeleted)
            throw new InvalidInputException();

        return userDevice;
    }


    private Task<Guid> GetCurrentDeviceIdAsync(CancellationToken ct) =>
        Task.FromResult(_identity.LocalDeviceId);
}
