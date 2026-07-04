using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Constants;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync;
using System.Text.Json;

using PasswordManagerLocal.Backend.Utils;

namespace PasswordManagerLocal.Backend.Services;

public sealed class OutgoingDeltaBuilderService : IOutgoingDeltaBuilderService
{
    private readonly IUserRepository _users;
    private readonly IGroupRepository _groups;
    private readonly IDeviceRepository _devices;
    private readonly IUserDeviceRepository _userDevices;
    private readonly ILocalUserDeviceRepository _localUserDevices;
    private readonly IDeviceIdentityService _identity;

    public OutgoingDeltaBuilderService(
        IUserRepository users,
        IGroupRepository groups,
        IDeviceRepository devices,
        IUserDeviceRepository userDevices,
        ILocalUserDeviceRepository localUserDevices,
        IDeviceIdentityService identity)
    {
        _users = users;
        _groups = groups;
        _devices = devices;
        _userDevices = userDevices;
        _localUserDevices = localUserDevices;
        _identity = identity;
    }




    public async Task<NetworkDelta> BuildAsync(SyncItem item, Device device, CancellationToken ct = default)
    {
        if (!_identity.IsSyncOn)
            throw new InvalidOperationException("Local synchronization is disabled.");

        if (device.Id == Guid.Empty)
            throw new InvalidOperationException("Target device is invalid.");

        if (device.PublicKey.Length == 0)
            throw new InvalidOperationException("Target device agreement key is missing.");

        if (!device.IsTrusted)
            throw new InvalidOperationException("Target device is not trusted.");

        if (device.IsBlocked)
            throw new InvalidOperationException("Target device is blocked.");

        if (IsLocalDevice(device))
            throw new InvalidOperationException("The local device cannot be a synchronization target.");

        var ts = item.ChangedAtTs > 0 ? item.ChangedAtTs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var payload = await BuildPayloadAsync(item, device.Id, ts, ct);
        SyncCryptoUtil.ValidatePayloadIntegrity(payload, ts);

        UtcDateTimeUtil.NormalizeObjectGraph(payload);
        var plaintextPayload = JsonSerializer.SerializeToUtf8Bytes(payload, DataCodec.JsonOpts);
        try
        {
            if (plaintextPayload.Length == 0 || plaintextPayload.Length > SyncConstants.MaxIncomingDeltaPayloadBytes)
                throw new InvalidDataException("Outgoing delta payload size is invalid.");

            var networkDelta = new NetworkDelta
            {
                Entity = BuildEntityName(payload),
                Ts = ts,
                DeviceId = _identity.DeviceIdHex,
                SignPub = _identity.SignPublicKey,
                RecipientDeviceId = device.Id.ToString("N"),
                EncryptionVersion = SyncConstants.SyncDeltaEncryptionVersion,
                PayloadHash = Hashing.SHA256Hash(plaintextPayload)
            };

            var associatedData = SyncCryptoUtil.BuildAssociatedData(networkDelta);
            networkDelta.Payload = _identity.EncryptForDevice(
                plaintextPayload,
                device.PublicKey,
                associatedData,
                out var ephemeralPublicKey,
                out var nonce,
                out var tag);
            networkDelta.EphemeralPublicKey = ephemeralPublicKey;
            networkDelta.Nonce = nonce;
            networkDelta.Tag = tag;

            NetDeltaSigner.FillSignature(networkDelta, _identity);
            return networkDelta;
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(plaintextPayload);
        }
    }


    private async Task<SyncDeltaPayload> BuildPayloadAsync(SyncItem item, Guid targetDeviceId, long timestamp, CancellationToken ct)
    {
        var payload = new SyncDeltaPayload
        {
            ModelId = item.ModelId,
            ModelType = item.ModelType,
            ChangeType = item.ChangeType
        };

        if (item.ModelType == SyncModelType.UserDevice)
        {
            var userDevice = await _userDevices.GetByModelIdAsync(item.ModelId, ct);
            if (userDevice is null)
                throw new InvalidOperationException("User device sync source was not found.");

            payload.UserDevice = CreateUserDevicePayload(userDevice);
            return payload;
        }

        if (item.ChangeType == SyncChangeType.Deleted)
            return payload;

        if (item.ModelType == SyncModelType.User)
        {
            var user = await _users.GetByIdAsNoTrackingWithRelationsAsync(item.ModelId, ct);
            if (user is null)
                throw new InvalidOperationException("User sync source was not found.");

            payload.User = CreateUserPayload(user, timestamp);
            return payload;
        }

        if (item.ModelType == SyncModelType.Group)
        {
            var group = await _groups.GetByIdAsNoTrackingWithUsersAsync(item.ModelId, ct);
            if (group is null)
                throw new InvalidOperationException("Group sync source was not found.");

            payload.Group = CreateGroupPayload(group, timestamp);
            return payload;
        }

        if (item.ModelType == SyncModelType.Device)
        {
            var sourceDevice = await _devices.GetByIdAsNoTrackingWithUsersAsync(item.ModelId, ct);
            if (sourceDevice is null)
                throw new InvalidOperationException("Device sync source was not found.");

            if (IsLocalDevice(sourceDevice))
                throw new InvalidOperationException("The local device cannot be synchronized as a stored device.");

            payload.Device = await CreateDevicePayloadAsync(sourceDevice, targetDeviceId, timestamp, ct);
            return payload;
        }

        throw new InvalidOperationException("Unknown sync model type.");
    }


    private UserSyncPayload CreateUserPayload(User user, long timestamp)
    {
        foreach (var link in user.UserDevices)
            link.VerifyIntegrity();

        var payload = new UserSyncPayload
        {
            UId = user.UId,
            UsernameHash = user.UsernameHash,
            UsernameSalt = user.UsernameSalt,
            PasswordSalt = user.PasswordSalt,
            EncryptedPayload = user.EncryptedPayload,
            EncryptedGeneralUserDataPayload = user.EncryptedGeneralUserDataPayload,
            EncryptedUserPasswordsDataPayload = user.EncryptedUserPasswordsDataPayload,
            EncryptedUserDevicesDataPayload = user.EncryptedUserDevicesDataPayload,
            UserDataLastModifiedAt = user.UserDataLastModifiedAt,
            GeneralUserDataLastModifiedAt = user.GeneralUserDataLastModifiedAt,
            UserPasswordsDataLastModifiedAt = user.UserPasswordsDataLastModifiedAt,
            UserDevicesDataLastModifiedAt = user.UserDevicesDataLastModifiedAt,
            GroupIds = user.Groups.Select(g => g.Id).Distinct().ToList(),
            DeviceIds = user.UserDevices.Where(ud => !ud.IsDeleted).Select(ud => ud.DeviceId).Append(_identity.LocalDeviceId).Distinct().ToList()
        };

        payload.IntegrityHash = SyncCryptoUtil.CalculateUserHash(payload, timestamp);
        return payload;
    }


    private GroupSyncPayload CreateGroupPayload(Group group, long timestamp)
    {
        var payload = new GroupSyncPayload
        {
            Id = group.Id,
            EncryptedPayload = group.EncryptedPayload,
            UserIds = group.Users.Select(u => u.UId).Distinct().ToList()
        };

        payload.IntegrityHash = SyncCryptoUtil.CalculateGroupHash(payload, timestamp);
        return payload;
    }


    private async Task<DeviceSyncPayload> CreateDevicePayloadAsync(Device device, Guid targetDeviceId, long timestamp, CancellationToken ct)
    {
        foreach (var link in device.UserDevices)
            link.VerifyIntegrity();

        var userIds = new List<Guid>();
        foreach (var userId in device.UserDevices
                     .Where(ud => !ud.IsDeleted && ud.IsSyncOn)
                     .Select(ud => ud.UserId)
                     .Where(id => id != Guid.Empty)
                     .Distinct())
        {
            if (!await _localUserDevices.IsSyncOnAsync(userId, ct))
                continue;

            if (await _userDevices.HasActiveLinkAsync(userId, targetDeviceId, ct))
                userIds.Add(userId);
        }

        var payload = new DeviceSyncPayload
        {
            Id = device.Id,
            PublicKey = device.PublicKey,
            SignPublicKey = device.SignPublicKey,
            TlsCertFingerprint = device.TlsCertFingerprint,
            DeviceType = device.DeviceType,
            LastKnownHash = device.LastKnownHash,
            LastSync = UtcDateTimeUtil.ToUtc(device.LastSync),
            LastSeen = UtcDateTimeUtil.ToUtc(device.LastSeen),
            IsTrusted = device.IsTrusted,
            IsBlocked = device.IsBlocked,
            BlockedReason = device.BlockedReason,
            BlockedAt = UtcDateTimeUtil.ToUtc(device.BlockedAt),
            InvalidSyncAttemptCount = device.InvalidSyncAttemptCount,
            LastInvalidSyncAttemptAt = UtcDateTimeUtil.ToUtc(device.LastInvalidSyncAttemptAt),
            UserIds = userIds
        };

        payload.IntegrityHash = SyncCryptoUtil.CalculateDeviceHash(payload, timestamp);
        return payload;
    }


    private UserDeviceSyncPayload CreateUserDevicePayload(UserDevice userDevice)
    {
        userDevice.VerifyIntegrity();
        return new UserDeviceSyncPayload
        {
            UserId = userDevice.UserId,
            DeviceId = userDevice.DeviceId,
            IsSyncOn = userDevice.IsSyncOn,
            IsDeleted = userDevice.IsDeleted,
            DeletedAt = UtcDateTimeUtil.ToUtc(userDevice.DeletedAt),
            IntegrityHash = userDevice.IntegrityHash.ToArray()
        };
    }


    private bool IsLocalDevice(Device device) =>
        device.Id == _identity.LocalDeviceId ||
        device.SignPublicKey.SequenceEqual(_identity.SignPublicKey) ||
        string.Equals(FingerprintUtil.NormalizeOrEmpty(device.TlsCertFingerprint), FingerprintUtil.NormalizeOrEmpty(_identity.FingerprintHex), StringComparison.OrdinalIgnoreCase);




    private string BuildEntityName(SyncDeltaPayload payload) =>
        $"{payload.ModelType}:{payload.ChangeType}:{payload.ModelId:N}";
}
