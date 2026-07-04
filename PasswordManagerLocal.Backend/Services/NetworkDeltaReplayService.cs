using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Projections;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Security;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models.Encrypted;
using static PasswordManagerLocal.Backend.Constants.DataLengthConstants;
using System.Security.Cryptography;
using System.Text.Json;
using static PasswordManagerLocal.Backend.Utils.DataCodec;

namespace PasswordManagerLocal.Backend.Services;

public sealed class NetworkDeltaReplayService : INetworkDeltaReplayService
{
    private readonly IUserRepository _users;
    private readonly IGroupRepository _groups;
    private readonly IDeviceRepository _devices;
    private readonly IUserDeviceRepository _userDevices;
    private readonly ISyncTombstoneRepository _tombstones;
    private readonly IDeviceIdentityService _identity;

    public NetworkDeltaReplayService(
        IUserRepository users,
        IGroupRepository groups,
        IDeviceRepository devices,
        IUserDeviceRepository userDevices,
        ISyncTombstoneRepository tombstones,
        IDeviceIdentityService identity)
    {
        _users = users;
        _groups = groups;
        _devices = devices;
        _userDevices = userDevices;
        _tombstones = tombstones;
        _identity = identity;
    }

    public async Task<bool> IsAlreadyAppliedAsync(SyncDeltaPayload payload, long ts, CancellationToken ct)
    {
        if (payload.ChangeType == SyncChangeType.Deleted && payload.ModelType != SyncModelType.UserDevice)
        {
            var tombstone = await _tombstones.GetAsync(payload.ModelId, payload.ModelType, ct);
            return tombstone is not null && tombstone.DeletedAtTs >= ts;
        }

        if (payload.ModelType == SyncModelType.User)
        {
            if (payload.User is null || payload.User.IntegrityHash.Length != Hashing.SHA256HashSizeInBytes)
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
            if (payload.Group is null || payload.Group.IntegrityHash.Length != Hashing.SHA256HashSizeInBytes)
                return false;

            var existing = await _groups.GetWithUserIdsAsNoTrackingAsync(payload.ModelId, ct);
            if (existing is null)
                return false;

            var existingPayload = CreateGroupSyncPayloadForHash(existing);
            var existingHash = SyncCryptoUtil.CalculateGroupHash(existingPayload, existing.LastModifiedAt.ToUnixTimeMilliseconds());
            return existingHash.SequenceEqual(payload.Group.IntegrityHash);
        }

        if (payload.ModelType == SyncModelType.Device)
        {
            if (payload.Device is null || payload.Device.IntegrityHash.Length != Hashing.SHA256HashSizeInBytes)
                return false;

            var existing = await _devices.GetByIdWithUserDevicesAsync(payload.ModelId, ct);
            if (existing is null)
                return false;

            var existingPayload = CreateDeviceSyncPayloadForHash(existing);
            var existingHash = SyncCryptoUtil.CalculateDeviceHash(existingPayload, existing.LastModifiedAt.ToUnixTimeMilliseconds());
            return existingHash.SequenceEqual(payload.Device.IntegrityHash);
        }

        if (payload.ModelType == SyncModelType.UserDevice)
        {
            if (payload.UserDevice is null || payload.UserDevice.IntegrityHash.Length != Hashing.SHA256HashSizeInBytes)
                return false;

            var existing = await _userDevices.GetAsync(payload.UserDevice.UserId, payload.UserDevice.DeviceId, ct);
            if (existing is null)
                return false;

            existing.VerifyIntegrity();
            return existing.IntegrityHash.SequenceEqual(payload.UserDevice.IntegrityHash);
        }

        return false;
    }


    public async Task<bool> IsBlockedByNewerTombstoneAsync(SyncDeltaPayload payload, long ts, CancellationToken ct)
    {
        var tombstone = await _tombstones.GetAsync(payload.ModelId, payload.ModelType, ct);
        return tombstone is not null && tombstone.DeletedAtTs >= ts;
    }


    private UserSyncPayload CreateUserSyncPayloadForHash(User user) =>
        new()
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


    private GroupSyncPayload CreateGroupSyncPayloadForHash(GroupWithUserIdsData group) =>
        new()
        {
            Id = group.Id,
            EncryptedPayload = group.EncryptedPayload,
            UserIds = group.UserIds
        };


    private DeviceSyncPayload CreateDeviceSyncPayloadForHash(Device device) =>
        new()
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
            UserIds = device.UserDevices.Where(ud => !ud.IsDeleted).Select(ud => ud.UserId).Distinct().ToList()
        };
}
