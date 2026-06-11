using PasswordManagerLocalBackend.Abstractions.Persistence;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Security;
using PasswordManagerLocalBackend.Sync;
using System.Security.Cryptography;
using System.Text.Json;
using static PasswordManagerLocalBackend.Utils.DataValidationUtil;

namespace PasswordManagerLocalBackend.Services;

public sealed class NetworkDeltaService : INetworkDeltaService
{
    private readonly IOutgoingDeltaBuilderService _outgoingDeltaBuilder;
    private readonly IUserRepository _users;
    private readonly IGroupRepository _groups;
    private readonly IDeviceRepository _devices;
    private readonly IUserDeviceRepository _userDevices;
    private readonly ISyncTombstoneRepository _tombstones;
    private readonly ISyncQueueRepository _syncQueue;
    private readonly ISyncQueueService _syncQueueService;
    private readonly ISyncDeviceIdentityService _syncDeviceIdentities;
    private readonly IDeviceIdentityService _identity;
    private readonly IAuthService _auth;
    private readonly IUnitOfWork _uow;

    public NetworkDeltaService(
        IOutgoingDeltaBuilderService outgoingDeltaBuilder,
        IUserRepository users,
        IGroupRepository groups,
        IDeviceRepository devices,
        IUserDeviceRepository userDevices,
        ISyncTombstoneRepository tombstones,
        ISyncQueueRepository syncQueue,
        ISyncQueueService syncQueueService,
        ISyncDeviceIdentityService syncDeviceIdentities,
        IDeviceIdentityService identity,
        IAuthService auth,
        IUnitOfWork uow)
    {
        _outgoingDeltaBuilder = outgoingDeltaBuilder;
        _users = users;
        _groups = groups;
        _devices = devices;
        _userDevices = userDevices;
        _tombstones = tombstones;
        _syncQueue = syncQueue;
        _syncQueueService = syncQueueService;
        _syncDeviceIdentities = syncDeviceIdentities;
        _identity = identity;
        _auth = auth;
        _uow = uow;
    }




    public Task<NetworkDelta> BuildAsync(SyncItem item, Device device, CancellationToken ct = default) =>
        _outgoingDeltaBuilder.BuildAsync(item, device, ct);


    public async Task<long> ApplyAsync(NetworkDelta delta, CancellationToken ct = default)
    {
        if (!_identity.IsSyncOn)
            throw new UnauthorizedAccessException("Local synchronization is disabled.");

        SyncCryptoUtil.ValidateEncryptedEnvelope(delta, _identity.LocalDeviceId);
        VerifyDeltaSignature(delta);
        var sourceDevice = await GetAndValidateSourceDeviceAsync(delta, ct);

        var plaintextPayload = DecryptPayload(delta);
        SyncDeltaPayload payload;
        try
        {
            payload = DeserializePayload(plaintextPayload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextPayload);
        }

        ValidateEnvelope(delta, payload);
        SyncCryptoUtil.ValidatePayloadIntegrity(payload, delta.Ts);
        await ValidateDeviceIdentityImmutabilityAsync(sourceDevice, payload, ct);
        await ValidateSourceCanApplyPayloadAsync(sourceDevice, payload, ct);

        if (await IsBlockedByNewerTombstoneAsync(payload, delta.Ts, ct))
        {
            await TouchSourceDeviceAsync(sourceDevice, ct);
            await _uow.SaveChangesAsync(ct);
            return delta.Ts;
        }

        if (await IsAlreadyAppliedAsync(payload, delta.Ts, ct))
        {
            await TouchSourceDeviceAsync(sourceDevice, ct);
            await _uow.SaveChangesAsync(ct);
            return delta.Ts;
        }

        var applied = payload.ModelType switch
        {
            SyncModelType.User => await ApplyUserAsync(payload, delta.Ts, ct),
            SyncModelType.Group => await ApplyGroupAsync(payload, delta.Ts, ct),
            SyncModelType.Device => await ApplyDeviceAsync(payload, delta.Ts, ct),
            SyncModelType.UserDevice => await ApplyUserDeviceAsync(payload, sourceDevice, delta.Ts, ct),
            _ => throw new InvalidOperationException("Unknown sync model type.")
        };

        if (!DeletesSourceDevice(payload, sourceDevice) && !DeletesLocalUserProfile(payload))
            await TouchSourceDeviceAsync(sourceDevice, ct);

        await _uow.SaveChangesAsync(ct);

        if (applied)
            await RefreshAffectedSessionCachesAsync(payload, ct);

        if (applied && ShouldPropagate(payload))
            await PropagateIncomingDeltaAsync(payload, sourceDevice.Id, delta.Ts, ct);

        return delta.Ts;
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


    private async Task<bool> ApplyUserAsync(SyncDeltaPayload delta, long ts, CancellationToken ct)
    {
        var existing = await _users.GetByIdWithRelationsAsync(delta.ModelId, ct);

        if (existing is not null && IsIncomingOlderOrSame(existing.LastModifiedAt, ts))
            return false;

        if (delta.ChangeType == SyncChangeType.Deleted)
        {
            if (existing is not null)
                _users.Delete(existing);

            await _tombstones.UpsertAsync(delta.ModelId, delta.ModelType, ts, ct);
            return true;
        }

        if (delta.User is null)
            throw new InvalidDataException("User sync payload is missing.");

        var user = existing ?? CreateUser(delta.User);
        CopyUserData(delta.User, user);
        user.LastModifiedAt = FromTimestamp(ts);

        if (existing is null)
            await _users.AddAsync(user, ct);

        await SyncUserGroupsAsync(user, delta.User.GroupIds, ct);
        await SyncUserDevicesAsync(user, delta.User.DeviceIds, user.LastModifiedAt, ct);
        user.GenerateIntegrityHash();
        await RemoveTombstoneAsync(delta, ct);
        return true;
    }


    private async Task<bool> ApplyGroupAsync(SyncDeltaPayload delta, long ts, CancellationToken ct)
    {
        var existing = await _groups.GetByIdWithUsersAsync(delta.ModelId, ct);

        if (existing is not null && IsIncomingOlderOrSame(existing.LastModifiedAt, ts))
            return false;

        if (delta.ChangeType == SyncChangeType.Deleted)
        {
            if (existing is not null)
                _groups.Delete(existing);

            await _tombstones.UpsertAsync(delta.ModelId, delta.ModelType, ts, ct);
            return true;
        }

        if (delta.Group is null)
            throw new InvalidDataException("Group sync payload is missing.");

        var group = existing ?? CreateGroup(delta.Group);
        CopyGroupData(delta.Group, group);
        group.LastModifiedAt = FromTimestamp(ts);

        if (existing is null)
            await _groups.AddAsync(group, ct);

        await SyncGroupUsersAsync(group, delta.Group.UserIds, ct);
        group.GenerateIntegrityHash();
        await RemoveTombstoneAsync(delta, ct);
        return true;
    }


    private async Task<bool> ApplyDeviceAsync(SyncDeltaPayload delta, long ts, CancellationToken ct)
    {
        if (IsLocalDevicePayload(delta))
            return false;

        var existing = await _devices.GetByIdWithUsersAsync(delta.ModelId, ct);

        if (existing is not null && IsIncomingOlderOrSame(existing.LastModifiedAt, ts))
            return false;

        if (delta.ChangeType == SyncChangeType.Deleted)
        {
            if (existing is not null)
            {
                _syncDeviceIdentities.TryRemove(existing);
                _devices.Delete(existing);
            }

            await _tombstones.UpsertAsync(delta.ModelId, delta.ModelType, ts, ct);
            return true;
        }

        if (delta.Device is null)
            throw new InvalidDataException("Device sync payload is missing.");

        var isNew = existing is null;
        var device = existing ?? CreateDevice(delta.Device);
        CopyDeviceData(delta.Device, device, isNew);
        device.LastModifiedAt = FromTimestamp(ts);

        if (isNew)
            await _devices.AddAsync(device, ct);

        await SyncDeviceUsersAsync(device, delta.Device.UserIds, device.LastModifiedAt, ct);
        device.GenerateIntegrityHash();
        await RemoveTombstoneAsync(delta, ct);

        await RefreshCachedDeviceAsync(device, ct);
        return true;
    }


    private async Task<bool> ApplyUserDeviceAsync(SyncDeltaPayload delta, Device sourceDevice, long ts, CancellationToken ct)
    {
        if (delta.UserDevice is null)
            throw new InvalidDataException("User device sync payload is missing.");

        ValidateUserDevicePayload(delta);

        if (DeletesLocalUserProfile(delta))
            return await ApplyLocalUserProfileDisconnectAsync(delta, ts, ct);

        if (IsLocalUserDevicePayload(delta))
            return false;

        var existing = await _userDevices.GetAsync(delta.UserDevice.UserId, delta.UserDevice.DeviceId, ct);

        if (existing is not null && IsIncomingOlderOrSame(existing.LastModifiedAt, ts))
            return false;

        var modifiedAt = FromTimestamp(ts);

        if (delta.ChangeType == SyncChangeType.Deleted || delta.UserDevice.IsDeleted)
        {
            if (existing is not null)
            {
                existing.IsDeleted = true;
                existing.IsSyncEnabled = false;
                existing.DeletedAt = delta.UserDevice.DeletedAt ?? modifiedAt;
                existing.LastModifiedAt = modifiedAt;
                _userDevices.Update(existing);
                await _tombstones.UpsertAsync(delta.ModelId, delta.ModelType, ts, ct);
            }
            else
            {
                await _tombstones.UpsertAsync(delta.ModelId, delta.ModelType, ts, ct);
            }

            await DeleteDeviceIfDetachedAsync(delta.UserDevice.DeviceId, ct);
            return true;
        }

        if (!await ResolveUserDeviceNameConflictAsync(delta.UserDevice, modifiedAt, ct))
            return false;

        var userDevice = existing ?? new UserDevice
        {
            UserId = delta.UserDevice.UserId,
            DeviceId = delta.UserDevice.DeviceId
        };

        userDevice.Name = delta.UserDevice.Name.Trim();
        userDevice.IsDeleted = false;
        userDevice.IsSyncEnabled = delta.UserDevice.IsSyncEnabled;
        userDevice.LinkedAt = delta.UserDevice.LinkedAt;
        userDevice.DeletedAt = null;
        userDevice.LastModifiedAt = modifiedAt;

        if (userDevice.DeviceId == sourceDevice.Id && !string.Equals(sourceDevice.DeviceName, userDevice.Name, StringComparison.Ordinal))
        {
            sourceDevice.DeviceName = userDevice.Name;
            sourceDevice.LastModifiedAt = modifiedAt;
            sourceDevice.GenerateIntegrityHash();
            _devices.Update(sourceDevice);
        }

        if (existing is null)
            await _userDevices.AddAsync(userDevice, ct);
        else
            _userDevices.Update(userDevice);

        await RemoveTombstoneAsync(delta, ct);
        return true;
    }




    private async Task<bool> ResolveUserDeviceNameConflictAsync(UserDeviceSyncPayload payload, DateTimeOffset modifiedAt, CancellationToken ct)
    {
        var conflict = await _userDevices.GetActiveByNameAsync(payload.UserId, payload.Name, payload.DeviceId, ct);
        if (conflict is null)
            return true;

        if (conflict.LastModifiedAt.ToUnixTimeMilliseconds() >= modifiedAt.ToUnixTimeMilliseconds())
            return false;

        conflict.Name = BuildUniqueFallbackUserDeviceName(conflict.UserId, conflict.DeviceId);
        conflict.LastModifiedAt = modifiedAt.AddTicks(-1);
        _userDevices.Update(conflict);
        return true;
    }


    private static string BuildUniqueFallbackUserDeviceName(Guid userId, Guid deviceId) =>
        $"Device {deviceId:N}"[..39];


    private async Task DeleteDeviceIfDetachedAsync(Guid deviceId, CancellationToken ct)
    {
        if (await _userDevices.HasAnyActiveLinkForDeviceAsync(deviceId, ct))
            return;

        var device = await _devices.GetByIdWithUserDevicesAsync(deviceId, ct);
        if (device is null)
            return;

        _syncDeviceIdentities.TryRemove(device);
        _devices.Delete(device);
    }


    private async Task<bool> ApplyLocalUserProfileDisconnectAsync(SyncDeltaPayload delta, long ts, CancellationToken ct)
    {
        if (delta.UserDevice is null)
            throw new InvalidDataException("User device sync payload is missing.");

        await _tombstones.UpsertAsync(delta.ModelId, delta.ModelType, ts, ct);

        var user = await _users.GetByIdWithRelationsAsync(delta.UserDevice.UserId, ct);
        if (user is null)
            return false;

        var relatedDeviceIds = user.UserDevices
            .Where(ud => ud.DeviceId != Guid.Empty && ud.DeviceId != _identity.LocalDeviceId)
            .Select(ud => ud.DeviceId)
            .Distinct()
            .ToList();

        _auth.LogoutUser(user.UId, AuthSessionInvalidationReason.ProfileRemoved);
        _users.Delete(user);

        foreach (var deviceId in relatedDeviceIds)
        {
            if (await _userDevices.HasAnyActiveLinkForDeviceExceptUserAsync(deviceId, user.UId, ct))
                continue;

            var device = await _devices.GetByIdWithUserDevicesAsync(deviceId, ct);
            if (device is null)
                continue;

            _syncDeviceIdentities.TryRemove(device);
            _devices.Delete(device);
        }

        return true;
    }


    private async Task SyncUserGroupsAsync(User user, IEnumerable<Guid> groupIds, CancellationToken ct)
    {
        var ids = CreateIdSet(groupIds);

        foreach (var group in user.Groups.Where(g => !ids.Contains(g.Id)).ToList())
            user.Groups.Remove(group);

        foreach (var id in ids)
        {
            if (user.Groups.Any(g => g.Id == id))
                continue;

            var group = await _groups.GetByIdAsync(id, ct);
            if (group is not null)
                user.Groups.Add(group);
        }
    }


    private async Task SyncUserDevicesAsync(User user, IEnumerable<Guid> deviceIds, DateTimeOffset modifiedAt, CancellationToken ct)
    {
        var ids = CreateIdSet(deviceIds);

        foreach (var userDevice in user.UserDevices.Where(ud => !ids.Contains(ud.DeviceId)).ToList())
        {
            userDevice.IsDeleted = true;
            userDevice.IsSyncEnabled = false;
            userDevice.DeletedAt = modifiedAt;
            userDevice.LastModifiedAt = modifiedAt;
        }

        foreach (var id in ids)
        {
            if (user.UserDevices.Any(ud => ud.DeviceId == id && !ud.IsDeleted))
                continue;

            var device = await _devices.GetByIdAsync(id, ct);
            if (device is not null)
            {
                user.UserDevices.Add(new UserDevice
                {
                    UserId = user.UId,
                    DeviceId = device.Id,
                    User = user,
                    Device = device,
                    Name = string.IsNullOrWhiteSpace(device.DeviceName) ? BuildUniqueFallbackUserDeviceName(user.UId, device.Id) : device.DeviceName,
                    LinkedAt = modifiedAt,
                    LastModifiedAt = modifiedAt
                });
            }
        }
    }


    private async Task SyncGroupUsersAsync(Group group, IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = CreateIdSet(userIds);

        foreach (var user in group.Users.Where(u => !ids.Contains(u.UId)).ToList())
            group.Users.Remove(user);

        foreach (var id in ids)
        {
            if (group.Users.Any(u => u.UId == id))
                continue;

            var user = await _users.GetByIdAsync(id, ct);
            if (user is not null)
                group.Users.Add(user);
        }
    }


    private async Task SyncDeviceUsersAsync(Device device, IEnumerable<Guid> userIds, DateTimeOffset modifiedAt, CancellationToken ct)
    {
        var ids = CreateIdSet(userIds);

        foreach (var userDevice in device.UserDevices.Where(ud => !ids.Contains(ud.UserId)).ToList())
        {
            userDevice.IsDeleted = true;
            userDevice.IsSyncEnabled = false;
            userDevice.DeletedAt = modifiedAt;
            userDevice.LastModifiedAt = modifiedAt;
        }

        foreach (var id in ids)
        {
            if (device.UserDevices.Any(ud => ud.UserId == id && !ud.IsDeleted))
                continue;

            var user = await _users.GetByIdAsync(id, ct);
            if (user is not null)
            {
                device.UserDevices.Add(new UserDevice
                {
                    UserId = user.UId,
                    DeviceId = device.Id,
                    User = user,
                    Device = device,
                    Name = string.IsNullOrWhiteSpace(device.DeviceName) ? BuildUniqueFallbackUserDeviceName(user.UId, device.Id) : device.DeviceName,
                    LinkedAt = modifiedAt,
                    LastModifiedAt = modifiedAt
                });
            }
        }
    }


    private async Task<Device> GetAndValidateSourceDeviceAsync(NetworkDelta delta, CancellationToken ct)
    {
        var sourceDevice = await _devices.GetBySignPublicKeyAsync(delta.SignPub, ct);
        if (sourceDevice is null)
            throw new UnauthorizedAccessException("Unknown sync source device.");

        if (sourceDevice.IsBlocked || !sourceDevice.IsTrusted)
            throw new UnauthorizedAccessException("Sync source device is not allowed.");

        if (!await _userDevices.HasAnyActiveSyncEnabledLinkForDeviceAsync(sourceDevice.Id, ct))
            throw new UnauthorizedAccessException("Sync source device is not linked to any active sync-enabled user.");

        if (!string.Equals(delta.DeviceId, BuildDeviceId(delta.SignPub), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Sync source device id is invalid.");

        return sourceDevice;
    }


    private async Task ValidateDeviceIdentityImmutabilityAsync(Device sourceDevice, SyncDeltaPayload payload, CancellationToken ct)
    {
        if (payload.ModelType != SyncModelType.Device || payload.Device is null)
            return;

        if (payload.Device.SignPublicKey.Length != PasswordManagerLocalBackend.Constants.SyncConstants.SyncDeltaEd25519PublicKeyBytes)
            throw new InvalidDataException("Device sync signing key is invalid.");

        if (payload.Device.PublicKey.Length != PasswordManagerLocalBackend.Constants.SyncConstants.SyncDeltaX25519PublicKeyBytes)
            throw new InvalidDataException("Device sync agreement key is invalid.");

        if (string.IsNullOrWhiteSpace(payload.Device.TlsCertFingerprint))
            throw new InvalidDataException("Device sync TLS fingerprint is missing.");

        if (payload.ModelId == sourceDevice.Id)
        {
            if (!payload.Device.SignPublicKey.SequenceEqual(sourceDevice.SignPublicKey))
                throw new InvalidDataException("Source device signing key cannot be changed by sync.");

            if (!payload.Device.PublicKey.SequenceEqual(sourceDevice.PublicKey))
                throw new InvalidDataException("Source device agreement key cannot be changed by sync.");

            if (!string.Equals(NormalizeFingerprint(payload.Device.TlsCertFingerprint), NormalizeFingerprint(sourceDevice.TlsCertFingerprint), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Source device TLS fingerprint cannot be changed by sync.");
        }

        var existing = await _devices.GetByIdAsync(payload.ModelId, ct);
        if (existing is null)
            return;

        if (!existing.SignPublicKey.SequenceEqual(payload.Device.SignPublicKey))
            throw new InvalidDataException("Existing device signing key cannot be changed by sync.");

        if (!existing.PublicKey.SequenceEqual(payload.Device.PublicKey))
            throw new InvalidDataException("Existing device agreement key cannot be changed by sync.");

        if (!string.Equals(NormalizeFingerprint(existing.TlsCertFingerprint), NormalizeFingerprint(payload.Device.TlsCertFingerprint), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Existing device TLS fingerprint cannot be changed by sync.");
    }


    private async Task ValidateSourceCanApplyPayloadAsync(Device sourceDevice, SyncDeltaPayload payload, CancellationToken ct)
    {
        if (payload.ModelType == SyncModelType.User)
        {
            if (!await _userDevices.HasActiveLinkAsync(payload.ModelId, sourceDevice.Id, ct))
                throw new UnauthorizedAccessException("Source device cannot modify this user.");

            return;
        }

        if (payload.ModelType == SyncModelType.Group)
        {
            var group = await _groups.GetByIdWithUsersAsync(payload.ModelId, ct);
            var userIds = group is not null
                ? group.Users.Select(u => u.UId).ToList()
                : payload.Group?.UserIds ?? [];

            if (userIds.Count == 0)
                throw new UnauthorizedAccessException("Source device cannot modify this group.");

            foreach (var userId in userIds)
            {
                if (await _userDevices.HasActiveLinkAsync(userId, sourceDevice.Id, ct))
                    return;
            }

            throw new UnauthorizedAccessException("Source device cannot modify this group.");
        }

        if (payload.ModelType == SyncModelType.Device)
        {
            if (IsLocalDevicePayload(payload))
                return;

            if (payload.ModelId == sourceDevice.Id)
                return;

            if (await _userDevices.SharesActiveUserAsync(sourceDevice.Id, payload.ModelId, ct))
                return;

            if (payload.Device is not null)
            {
                foreach (var userId in payload.Device.UserIds.Where(id => id != Guid.Empty).Distinct())
                {
                    if (await _userDevices.HasActiveLinkAsync(userId, sourceDevice.Id, ct))
                        return;
                }
            }

            throw new UnauthorizedAccessException("Source device cannot modify this device.");
        }

        if (payload.ModelType == SyncModelType.UserDevice)
        {
            if (payload.UserDevice is null)
                throw new InvalidDataException("User device sync payload is missing.");

            ValidateUserDevicePayload(payload);

            if (!await _userDevices.HasActiveLinkAsync(payload.UserDevice.UserId, sourceDevice.Id, ct))
                throw new UnauthorizedAccessException("Source device cannot modify this user-device link.");
        }
    }


    private async Task TouchSourceDeviceAsync(Device sourceDevice, CancellationToken ct)
    {
        sourceDevice.LastSync = DateTime.UtcNow;
        sourceDevice.LastSeen = DateTime.UtcNow;
        sourceDevice.InvalidSyncAttemptCount = 0;
        sourceDevice.LastInvalidSyncAttemptAt = null;
        sourceDevice.GenerateIntegrityHash();

        await RefreshCachedDeviceAsync(sourceDevice, ct);
    }


    private static bool DeletesSourceDevice(SyncDeltaPayload payload, Device sourceDevice) =>
        payload.ModelType == SyncModelType.Device &&
        payload.ChangeType == SyncChangeType.Deleted &&
        payload.ModelId == sourceDevice.Id;


    private bool ShouldPropagate(SyncDeltaPayload payload) =>
        !DeletesLocalUserProfile(payload) &&
        !IsLocalDevicePayload(payload);


    private bool DeletesLocalUserProfile(SyncDeltaPayload payload) =>
        payload.ModelType == SyncModelType.UserDevice &&
        payload.ChangeType == SyncChangeType.Deleted &&
        payload.UserDevice is not null &&
        payload.UserDevice.IsDeleted &&
        payload.UserDevice.DeviceId == _identity.LocalDeviceId;


    private bool IsLocalUserDevicePayload(SyncDeltaPayload payload) =>
        payload.ModelType == SyncModelType.UserDevice &&
        payload.UserDevice is not null &&
        payload.UserDevice.DeviceId == _identity.LocalDeviceId;


    private UserSyncPayload CreateUserSyncPayloadForHash(User user) =>
        new()
        {
            UId = user.UId,
            UsernameHash = user.UsernameHash,
            UsernameSalt = user.UsernameSalt,
            PasswordSalt = user.PasswordSalt,
            EncryptedPayload = user.EncryptedPayload,
            GroupIds = user.Groups.Select(g => g.Id).Distinct().ToList(),
            DeviceIds = user.UserDevices.Where(ud => !ud.IsDeleted && !IsLocalDeviceId(ud.DeviceId)).Select(ud => ud.DeviceId).Distinct().ToList()
        };


    private static GroupSyncPayload CreateGroupSyncPayloadForHash(Group group) =>
        new()
        {
            Id = group.Id,
            EncryptedPayload = group.EncryptedPayload,
            UserIds = group.Users.Select(u => u.UId).Distinct().ToList()
        };


    private static DeviceSyncPayload CreateDeviceSyncPayloadForHash(Device device) =>
        new()
        {
            Id = device.Id,
            PublicKey = device.PublicKey,
            SignPublicKey = device.SignPublicKey,
            TlsCertFingerprint = device.TlsCertFingerprint,
            DeviceName = device.DeviceName,
            LastKnownHash = device.LastKnownHash,
            LastSync = device.LastSync,
            LastSeen = device.LastSeen,
            IsTrusted = device.IsTrusted,
            IsBlocked = device.IsBlocked,
            BlockedReason = device.BlockedReason,
            BlockedAt = device.BlockedAt,
            InvalidSyncAttemptCount = device.InvalidSyncAttemptCount,
            LastInvalidSyncAttemptAt = device.LastInvalidSyncAttemptAt,
            UserIds = device.UserDevices.Where(ud => !ud.IsDeleted).Select(ud => ud.UserId).Distinct().ToList()
        };


    private bool IsLocalDeviceId(Guid deviceId) =>
        deviceId == _identity.LocalDeviceId;


    private bool IsLocalDevicePayload(SyncDeltaPayload payload) =>
        payload.ModelType == SyncModelType.Device &&
        (payload.ModelId == _identity.LocalDeviceId || IsLocalDevicePayload(payload.Device));


    private bool IsLocalDevicePayload(DeviceSyncPayload? device) =>
        device is not null &&
        (device.Id == _identity.LocalDeviceId ||
         device.SignPublicKey.SequenceEqual(_identity.SignPublicKey) ||
         string.Equals(NormalizeFingerprint(device.TlsCertFingerprint), NormalizeFingerprint(_identity.FingerprintHex), StringComparison.OrdinalIgnoreCase));


    private async Task RefreshCachedDeviceAsync(Device device, CancellationToken ct)
    {
        _syncDeviceIdentities.TryRemove(device);

        if (!device.IsTrusted || device.IsBlocked)
            return;

        if (!await _userDevices.HasAnyActiveSyncEnabledLinkForDeviceAsync(device.Id, ct))
            return;

        if (await _syncQueue.HasPendingForDeviceAsync(device.Id, ct))
            _syncDeviceIdentities.TryAdd(device);
    }


    private async Task<bool> IsAlreadyAppliedAsync(SyncDeltaPayload payload, long ts, CancellationToken ct)
    {
        if (payload.ChangeType == SyncChangeType.Deleted && payload.ModelType != SyncModelType.UserDevice)
        {
            var tombstone = await _tombstones.GetAsync(payload.ModelId, payload.ModelType, ct);
            return tombstone is not null && tombstone.DeletedAtTs >= ts;
        }

        if (payload.ModelType == SyncModelType.User)
        {
            if (payload.User is null || payload.User.IntegrityHash.Length == 0)
                return false;

            var existing = await _users.GetByIdWithRelationsAsync(payload.ModelId, ct);
            if (existing is null)
                return false;

            var existingPayload = CreateUserSyncPayloadForHash(existing);
            var existingHash = SyncCryptoUtil.CalculateUserHash(existingPayload, existing.LastModifiedAt.ToUnixTimeMilliseconds());
            return existingHash.SequenceEqual(payload.User.IntegrityHash);
        }

        if (payload.ModelType == SyncModelType.Group)
        {
            if (payload.Group is null || payload.Group.IntegrityHash.Length == 0)
                return false;

            var existing = await _groups.GetByIdWithUsersAsync(payload.ModelId, ct);
            if (existing is null)
                return false;

            var existingPayload = CreateGroupSyncPayloadForHash(existing);
            var existingHash = SyncCryptoUtil.CalculateGroupHash(existingPayload, existing.LastModifiedAt.ToUnixTimeMilliseconds());
            return existingHash.SequenceEqual(payload.Group.IntegrityHash);
        }

        if (payload.ModelType == SyncModelType.Device)
        {
            if (payload.Device is null || payload.Device.IntegrityHash.Length == 0)
                return false;

            var existing = await _devices.GetByIdWithUsersAsync(payload.ModelId, ct);
            if (existing is null)
                return false;

            var existingPayload = CreateDeviceSyncPayloadForHash(existing);
            var existingHash = SyncCryptoUtil.CalculateDeviceHash(existingPayload, existing.LastModifiedAt.ToUnixTimeMilliseconds());
            return existingHash.SequenceEqual(payload.Device.IntegrityHash);
        }

        if (payload.ModelType == SyncModelType.UserDevice)
        {
            if (payload.UserDevice is null || payload.UserDevice.IntegrityHash.Length == 0)
                return false;

            var existing = await _userDevices.GetAsync(payload.UserDevice.UserId, payload.UserDevice.DeviceId, ct);
            if (existing is null)
                return false;

            return SyncHashUtil.CalculateUserDeviceHash(existing).SequenceEqual(payload.UserDevice.IntegrityHash);
        }

        return false;
    }


    private Task PropagateIncomingDeltaAsync(SyncDeltaPayload payload, Guid sourceDeviceId, long changedAtTs, CancellationToken ct) =>
        _syncQueueService.EnqueuePropagationAsync(new SyncItem
        {
            ModelId = payload.ModelId,
            ModelType = payload.ModelType,
            ChangeType = payload.ChangeType,
            ChangedAtTs = changedAtTs
        }, sourceDeviceId, changedAtTs, ct);


    private async Task<bool> IsBlockedByNewerTombstoneAsync(SyncDeltaPayload payload, long ts, CancellationToken ct)
    {
        var tombstone = await _tombstones.GetAsync(payload.ModelId, payload.ModelType, ct);
        return tombstone is not null && tombstone.DeletedAtTs >= ts;
    }


    private async Task RemoveTombstoneAsync(SyncDeltaPayload payload, CancellationToken ct)
    {
        var tombstone = await _tombstones.GetAsync(payload.ModelId, payload.ModelType, ct);
        if (tombstone is not null)
            _tombstones.Delete(tombstone);
    }


    private static User CreateUser(UserSyncPayload payload) =>
        new()
        {
            UId = payload.UId
        };


    private static Group CreateGroup(GroupSyncPayload payload) =>
        new()
        {
            Id = payload.Id
        };


    private static Device CreateDevice(DeviceSyncPayload payload) =>
        new()
        {
            Id = payload.Id
        };


    private static void CopyUserData(UserSyncPayload source, User target)
    {
        target.UId = source.UId;
        target.UsernameHash = source.UsernameHash;
        target.UsernameSalt = source.UsernameSalt;
        target.PasswordSalt = source.PasswordSalt;
        target.EncryptedPayload = source.EncryptedPayload;
        target.IntegrityHash = source.IntegrityHash;
    }


    private static void CopyGroupData(GroupSyncPayload source, Group target)
    {
        target.Id = source.Id;
        target.EncryptedPayload = source.EncryptedPayload;
        target.IntegrityHash = source.IntegrityHash;
    }


    private static void CopyDeviceData(DeviceSyncPayload source, Device target, bool isNew)
    {
        target.Id = source.Id;
        target.PublicKey = source.PublicKey;
        target.SignPublicKey = source.SignPublicKey;
        target.TlsCertFingerprint = source.TlsCertFingerprint;
        target.DeviceName = source.DeviceName;
        target.LastKnownHash = source.LastKnownHash;
        target.IntegrityHash = source.IntegrityHash;

        if (isNew)
        {
            target.LastSync = source.LastSync;
            target.LastSeen = source.LastSeen;
            target.IsTrusted = source.IsTrusted;
            target.IsBlocked = source.IsBlocked;
            target.BlockedReason = source.BlockedReason;
            target.BlockedAt = source.BlockedAt;
            target.InvalidSyncAttemptCount = source.InvalidSyncAttemptCount;
            target.LastInvalidSyncAttemptAt = source.LastInvalidSyncAttemptAt;
            return;
        }

        if (source.IsBlocked)
        {
            target.IsBlocked = true;
            target.BlockedReason = source.BlockedReason;
            target.BlockedAt = source.BlockedAt ?? DateTimeOffset.UtcNow;
        }
    }


    private byte[] DecryptPayload(NetworkDelta delta)
    {
        try
        {
            var associatedData = SyncCryptoUtil.BuildAssociatedData(delta);
            var plaintext = _identity.DecryptFromDevice(
                delta.Payload,
                delta.EphemeralPublicKey,
                delta.Nonce,
                delta.Tag,
                associatedData);

            SyncCryptoUtil.ValidatePlaintextHash(plaintext, delta.PayloadHash);
            return plaintext;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException("Network delta payload decryption failed.", ex);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("Network delta encryption key material is invalid.", ex);
        }
    }


    private static SyncDeltaPayload DeserializePayload(byte[] plaintextPayload)
    {
        if (plaintextPayload.Length == 0)
            throw new InvalidDataException("Network delta payload is empty.");

        RejectSavedKeyPayload(plaintextPayload);

        var payload = JsonSerializer.Deserialize<SyncDeltaPayload>(plaintextPayload);
        if (payload is null)
            throw new InvalidDataException("Network delta payload is invalid.");

        return payload;
    }


    private static void ValidateEnvelope(NetworkDelta delta, SyncDeltaPayload payload)
    {
        if (delta.Ts <= 0)
            throw new InvalidDataException("Network delta timestamp is invalid.");

        if (payload.ModelId == Guid.Empty)
            throw new InvalidDataException("Network delta model id is invalid.");

        if (!Enum.IsDefined(payload.ModelType))
            throw new InvalidDataException("Network delta model type is invalid.");

        if (!Enum.IsDefined(payload.ChangeType))
            throw new InvalidDataException("Network delta change type is invalid.");

        var expectedEntity = $"{payload.ModelType}:{payload.ChangeType}:{payload.ModelId:N}";
        if (!string.Equals(delta.Entity, expectedEntity, StringComparison.Ordinal))
            throw new InvalidDataException("Network delta entity envelope is invalid.");

        if (payload.User is not null && (payload.ModelType != SyncModelType.User || payload.User.UId != payload.ModelId))
            throw new InvalidDataException("User sync payload envelope is invalid.");

        if (payload.Group is not null && (payload.ModelType != SyncModelType.Group || payload.Group.Id != payload.ModelId))
            throw new InvalidDataException("Group sync payload envelope is invalid.");

        if (payload.Device is not null && (payload.ModelType != SyncModelType.Device || payload.Device.Id != payload.ModelId))
            throw new InvalidDataException("Device sync payload envelope is invalid.");

        if (payload.UserDevice is not null)
        {
            if (payload.ModelType != SyncModelType.UserDevice)
                throw new InvalidDataException("User device sync payload envelope is invalid.");

            var expectedModelId = SyncIdentityUtil.BuildUserDeviceModelId(payload.UserDevice.UserId, payload.UserDevice.DeviceId);
            if (payload.ModelId != expectedModelId)
                throw new InvalidDataException("User device sync model id is invalid.");
        }
    }


    private static void ValidateUserDevicePayload(SyncDeltaPayload payload)
    {
        if (payload.UserDevice is null)
            throw new InvalidDataException("User device sync payload is missing.");

        if (payload.UserDevice.UserId == Guid.Empty || payload.UserDevice.DeviceId == Guid.Empty)
            throw new InvalidDataException("User device sync payload contains an invalid id.");

        var expectedModelId = SyncIdentityUtil.BuildUserDeviceModelId(payload.UserDevice.UserId, payload.UserDevice.DeviceId);
        if (payload.ModelId != expectedModelId)
            throw new InvalidDataException("User device sync model id is invalid.");

        if (payload.ChangeType == SyncChangeType.Deleted && !payload.UserDevice.IsDeleted)
            throw new InvalidDataException("Deleted user-device delta must contain a deleted link payload.");

        if (payload.ChangeType == SyncChangeType.Deleted && payload.UserDevice.IsSyncEnabled)
            throw new InvalidDataException("Deleted user-device delta cannot keep synchronization enabled.");

        if (payload.ChangeType == SyncChangeType.Deleted && payload.UserDevice.DeletedAt is null)
            throw new InvalidDataException("Deleted user-device delta must contain deletion time.");

        if (!payload.UserDevice.IsDeleted && !IsValidUserDeviceName(payload.UserDevice.Name))
            throw new InvalidDataException("User device name is invalid.");

        if (payload.UserDevice.IntegrityHash.Length == 0)
            throw new InvalidDataException("User device sync hash is missing.");
    }


    private static void VerifyDeltaSignature(NetworkDelta delta)
    {
        if (delta.SignPub.Length != PasswordManagerLocalBackend.Constants.SyncConstants.SyncDeltaEd25519PublicKeyBytes ||
            delta.Sig.Length != PasswordManagerLocalBackend.Constants.SyncConstants.SyncDeltaEd25519SignatureBytes)
            throw new InvalidDataException("Network delta signature is incomplete.");

        try
        {
            if (!NetDeltaSigner.VerifySignature(delta))
                throw new InvalidDataException("Network delta signature is invalid.");
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("Network delta signature is invalid.", ex);
        }
    }


    private static void RejectSavedKeyPayload(byte[] payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (ContainsProperty(doc.RootElement, "SavedKey"))
                throw new InvalidDataException("Network delta contains a local saved key.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Network delta payload is invalid.", ex);
        }
    }


    private static bool ContainsProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (ContainsProperty(property.Value, propertyName))
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (ContainsProperty(item, propertyName))
                    return true;
            }
        }

        return false;
    }


    private static string BuildDeviceId(byte[] signPublicKey) =>
        Convert.ToHexString(Hashing.SHA256Hash(signPublicKey));


    private static string NormalizeFingerprint(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
            return string.Empty;

        return fingerprint.Replace(":", string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();
    }


    private static DateTimeOffset FromTimestamp(long ts) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ts);


    private static bool IsIncomingOlderOrSame(DateTimeOffset local, long incomingTs) =>
        local.ToUnixTimeMilliseconds() >= incomingTs;


    private static HashSet<Guid> CreateIdSet(IEnumerable<Guid>? ids) =>
        ids?.Where(id => id != Guid.Empty).Distinct().ToHashSet() ?? [];
}
