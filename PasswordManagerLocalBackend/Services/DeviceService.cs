using PasswordManagerLocalBackend.Abstractions.Persistence;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models;
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





    public Task<LocalDeviceInfoResponse> GetLocalDeviceInfoAsync(CancellationToken ct = default) =>
        Task.FromResult(new LocalDeviceInfoResponse
        {
            DeviceId = _identity.LocalDeviceId,
            DeviceName = _identity.DeviceName,
            TlsCertFingerprint = _identity.FingerprintHex,
            IsSyncOn = _identity.IsSyncOn,
            CreatedAt = _identity.CreatedAt
        });


    public Task<bool> GetLocalDeviceSyncEnabledAsync(CancellationToken ct = default) =>
        Task.FromResult(_identity.IsSyncOn);


    public Task SetLocalDeviceSyncEnabledAsync(bool isSyncOn, CancellationToken ct = default) =>
        _syncRuntime.SetSyncEnabledAsync(isSyncOn, ct);


    public async Task SetLocalDeviceNameAsync(Guid token, string name, CancellationToken ct = default)
    {
        var normalizedName = NormalizeUserDeviceName(name);
        var user = await _users.GetAndVerifyUserAsync(token, ct);
        var userDevice = await EnsureLocalUserDeviceAsync(user.UId, ct);

        if (await _userDevices.IsNameTakenAsync(user.UId, normalizedName, _identity.LocalDeviceId, ct))
            throw new InvalidInputException();

        if (string.Equals(_identity.DeviceName, normalizedName, StringComparison.Ordinal) &&
            string.Equals(userDevice.Name, normalizedName, StringComparison.Ordinal))
            return;

        await _identity.SetDeviceNameAsync(normalizedName, ct);

        var device = await EnsureLocalDeviceAsync(ct);
        var now = DateTimeOffset.UtcNow;

        device.DeviceName = normalizedName;
        device.LastSeen = now.UtcDateTime;
        device.LastModifiedAt = now;
        device.GenerateIntegrityHash();
        _devices.Update(device);

        userDevice.Name = normalizedName;
        userDevice.LastModifiedAt = now;
        _userDevices.Update(userDevice);

        await _syncQueue.EnqueueAsync(new SyncItem
        {
            ModelId = SyncIdentityUtil.BuildUserDeviceModelId(userDevice.UserId, userDevice.DeviceId),
            ModelType = SyncModelType.UserDevice,
            ChangeType = SyncChangeType.Updated
        }, ct);
    }




    public async Task<IReadOnlyList<UserDeviceInfoResponse>> GetUserDevicesAsync(Guid token, CancellationToken ct = default)
    {
        var user = await _users.GetAndVerifyUserAsync(token, ct);
        await EnsureLocalUserDeviceAsync(user.UId, ct);
        var currentDeviceId = await GetCurrentDeviceIdAsync(ct);
        var userDevices = await _userDevices.ListByUserAsync(user.UId, ct);

        return userDevices
            .Where(ud => !ud.IsDeleted && ud.Device is not null)
            .Select(ud => UserDeviceInfoResponse.FromUserDevice(ud, currentDeviceId, _identity.IsSyncOn))
            .OrderByDescending(d => d.IsCurrentDevice)
            .ThenByDescending(d => d.LastSeen)
            .ToList();
    }




    public async Task SetUserDeviceNameAsync(Guid token, Guid deviceId, string name, CancellationToken ct = default)
    {
        var normalizedName = NormalizeUserDeviceName(name);

        if (deviceId == _identity.LocalDeviceId)
        {
            await SetLocalDeviceNameAsync(token, normalizedName, ct);
            return;
        }

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






    private async Task<UserDevice> EnsureLocalUserDeviceAsync(Guid userId, CancellationToken ct)
    {
        var device = await EnsureLocalDeviceAsync(ct);
        var userDevice = await _userDevices.GetAsync(userId, device.Id, ct);
        var now = DateTimeOffset.UtcNow;

        if (userDevice is null)
        {
            userDevice = new UserDevice
            {
                UserId = userId,
                DeviceId = device.Id,
                Device = device,
                Name = await BuildUniqueLocalUserDeviceNameAsync(userId, device.Id, _identity.DeviceName, ct),
                IsSyncEnabled = true,
                IsDeleted = false,
                LinkedAt = now,
                LastModifiedAt = now
            };

            await _userDevices.AddAsync(userDevice, ct);
            await _uow.SaveChangesAsync(ct);

            await EnqueueUserDeviceChangeAsync(userDevice, SyncChangeType.Created, ct);
            return userDevice;
        }

        var changed = false;
        if (userDevice.IsDeleted)
        {
            userDevice.IsDeleted = false;
            userDevice.DeletedAt = null;
            userDevice.LinkedAt = now;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(userDevice.Name))
        {
            userDevice.Name = await BuildUniqueLocalUserDeviceNameAsync(userId, device.Id, _identity.DeviceName, ct);
            changed = true;
        }

        if (changed)
        {
            userDevice.LastModifiedAt = now;
            _userDevices.Update(userDevice);
            await _uow.SaveChangesAsync(ct);
            await EnqueueUserDeviceChangeAsync(userDevice, SyncChangeType.Updated, ct);
        }

        return userDevice;
    }


    private Task EnqueueUserDeviceChangeAsync(UserDevice userDevice, SyncChangeType changeType, CancellationToken ct) =>
        _syncQueue.EnqueueAsync(new SyncItem
        {
            ModelId = SyncIdentityUtil.BuildUserDeviceModelId(userDevice.UserId, userDevice.DeviceId),
            ModelType = SyncModelType.UserDevice,
            ChangeType = changeType
        }, ct);


    private async Task<Device> EnsureLocalDeviceAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var localSelfDevices = await _devices.ListLocalSelfDevicesAsync(_identity.LocalDeviceId, _identity.SignPublicKey, _identity.FingerprintHex, ct);
        var duplicateLocalDevices = localSelfDevices
            .Where(d => d.Id != _identity.LocalDeviceId)
            .ToList();

        if (duplicateLocalDevices.Count != 0)
        {
            foreach (var duplicateDevice in duplicateLocalDevices)
                _devices.Delete(duplicateDevice);

            await _uow.SaveChangesAsync(ct);
        }

        var device = localSelfDevices.FirstOrDefault(d => d.Id == _identity.LocalDeviceId)
            ?? await _devices.GetByIdWithUserDevicesAsync(_identity.LocalDeviceId, ct);

        if (device is null)
        {
            device = new Device
            {
                Id = _identity.LocalDeviceId,
                PublicKey = _identity.AgreementPublicKey,
                SignPublicKey = _identity.SignPublicKey,
                TlsCertFingerprint = _identity.FingerprintHex,
                DeviceName = _identity.DeviceName,
                LastSync = now.UtcDateTime,
                LastSeen = now.UtcDateTime,
                IsTrusted = true,
                IsBlocked = false,
                LastModifiedAt = now
            };

            device.GenerateIntegrityHash();
            await _devices.AddAsync(device, ct);
            await _uow.SaveChangesAsync(ct);
            return device;
        }

        var changed = false;
        if (!device.PublicKey.SequenceEqual(_identity.AgreementPublicKey))
        {
            device.PublicKey = _identity.AgreementPublicKey;
            changed = true;
        }

        if (!device.SignPublicKey.SequenceEqual(_identity.SignPublicKey))
        {
            device.SignPublicKey = _identity.SignPublicKey;
            changed = true;
        }

        if (!string.Equals(device.TlsCertFingerprint, _identity.FingerprintHex, StringComparison.OrdinalIgnoreCase))
        {
            device.TlsCertFingerprint = _identity.FingerprintHex;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(device.DeviceName) || !string.Equals(device.DeviceName, _identity.DeviceName, StringComparison.Ordinal))
        {
            device.DeviceName = _identity.DeviceName;
            changed = true;
        }

        if (!device.IsTrusted || device.IsBlocked)
        {
            device.IsTrusted = true;
            device.IsBlocked = false;
            device.BlockedReason = null;
            device.BlockedAt = null;
            changed = true;
        }

        if (changed)
        {
            device.LastModifiedAt = now;
            device.GenerateIntegrityHash();
            _devices.Update(device);
            await _uow.SaveChangesAsync(ct);
        }

        return device;
    }


    private async Task<string> BuildUniqueLocalUserDeviceNameAsync(Guid userId, Guid deviceId, string requestedName, CancellationToken ct)
    {
        var baseName = string.IsNullOrWhiteSpace(requestedName)
            ? DeviceNameUtil.BuildDefaultDeviceName(deviceId)
            : requestedName.Trim();

        if (!await _userDevices.IsNameTakenAsync(userId, baseName, deviceId, ct))
            return baseName;

        for (var i = 2; i < 100; i++)
        {
            var suffix = $"-{i}";
            var prefixLength = Math.Min(baseName.Length, 64 - suffix.Length);
            var name = baseName[..prefixLength] + suffix;

            if (!await _userDevices.IsNameTakenAsync(userId, name, deviceId, ct))
                return name;
        }

        throw new InvalidInputException();
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
