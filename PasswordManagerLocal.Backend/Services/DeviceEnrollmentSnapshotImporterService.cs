using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync.Enrollment;
using PasswordManagerLocal.Backend.Utils;
using Google.Protobuf;
using PasswordManagerLocal.Backend.Constants;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Responses;
using PasswordManagerLocal.Backend.Sync;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using static PasswordManagerLocal.Backend.Constants.SyncConstants;
using PasswordManagerLocal.Backend.Abstractions.Caching;
using PasswordManagerLocal.Backend.State;

namespace PasswordManagerLocal.Backend.Services;

public sealed class DeviceEnrollmentSnapshotImporterService : IDeviceEnrollmentSnapshotImporterService
{
    private readonly IDeviceIdentityService _identity;
    private readonly IDeviceEnrollmentLocalLinkService _localLinks;

    public DeviceEnrollmentSnapshotImporterService(
        IDeviceIdentityService identity,
        IDeviceEnrollmentLocalLinkService localLinks)
    {
        _identity = identity;
        _localLinks = localLinks;
    }

    public async Task ImportAsync(IServiceProvider services, DeviceEnrollmentSnapshot snapshot, CancellationToken ct = default)
    {
        var devices = services.GetRequiredService<IDeviceRepository>();
        var users = services.GetRequiredService<IUserRepository>();
        var groups = services.GetRequiredService<IGroupRepository>();
        var userDevices = services.GetRequiredService<IUserDeviceRepository>();
        var localUserDevices = services.GetRequiredService<ILocalUserDeviceRepository>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var syncIdentities = services.GetRequiredService<ISyncDeviceIdentityService>();
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);

        await _localLinks.RemoveLocalDeviceRowsAsync(devices, ct);

        foreach (var deviceSnapshot in snapshot.Devices)
        {
            if (!DeviceTypeDetector.IsValid(deviceSnapshot.DeviceType))
                throw new InvalidDataException("The enrollment snapshot contains an invalid device type.");

            var isLocalDevice = deviceSnapshot.Id == _identity.LocalDeviceId ||
                deviceSnapshot.SignPublicKey.SequenceEqual(_identity.SignPublicKey) ||
                string.Equals(FingerprintUtil.Normalize(deviceSnapshot.TlsCertFingerprint), FingerprintUtil.Normalize(_identity.FingerprintHex), StringComparison.OrdinalIgnoreCase);

            if (isLocalDevice)
                continue;

            var device = await FindExistingDeviceForSnapshotAsync(devices, deviceSnapshot, ct);
            if (device is null)
            {
                device = new Device { Id = deviceSnapshot.Id };
                await devices.AddAsync(device, ct);
            }

            device.PublicKey = deviceSnapshot.PublicKey;
            device.SignPublicKey = deviceSnapshot.SignPublicKey;
            device.TlsCertFingerprint = FingerprintUtil.Normalize(deviceSnapshot.TlsCertFingerprint);
            device.DeviceType = deviceSnapshot.DeviceType;
            device.LastKnownHash = deviceSnapshot.LastKnownHash;
            device.LastSync = UtcDateTimeUtil.ToUtc(deviceSnapshot.LastSync);
            device.LastSeen = UtcDateTimeUtil.ToUtc(deviceSnapshot.LastSeen);
            device.IsTrusted = true;
            device.IsBlocked = deviceSnapshot.IsBlocked;
            device.BlockedReason = deviceSnapshot.BlockedReason;
            device.BlockedAt = UtcDateTimeUtil.ToUtc(deviceSnapshot.BlockedAt);
            device.InvalidSyncAttemptCount = deviceSnapshot.InvalidSyncAttemptCount;
            device.LastInvalidSyncAttemptAt = UtcDateTimeUtil.ToUtc(deviceSnapshot.LastInvalidSyncAttemptAt);
            device.LastModifiedAt = UtcDateTimeUtil.ToUtc(deviceSnapshot.LastModifiedAt == default ? now : deviceSnapshot.LastModifiedAt);
            device.GenerateIntegrityHash();
        }

        await unitOfWork.SaveChangesAsync(ct);

        foreach (var userSnapshot in snapshot.Users)
        {
            var user = await users.GetByIdAsync(userSnapshot.UId, ct);
            if (user is null)
            {
                user = new User { UId = userSnapshot.UId };
                await users.AddAsync(user, ct);
            }

            if (userSnapshot.EncryptedPayload.Length == 0 ||
                userSnapshot.EncryptedGeneralUserDataPayload.Length == 0 ||
                userSnapshot.EncryptedUserPasswordsDataPayload.Length == 0 ||
                userSnapshot.EncryptedUserDevicesDataPayload.Length == 0 ||
                userSnapshot.IntegrityHash.Length != Hashing.SHA256HashSizeInBytes)
                throw new InvalidDataException("The enrollment snapshot contains incomplete encrypted user data.");

            user.UsernameHash = userSnapshot.UsernameHash;
            user.UsernameSalt = userSnapshot.UsernameSalt;
            user.PasswordSalt = userSnapshot.PasswordSalt;
            user.EncryptedPayload = userSnapshot.EncryptedPayload;
            user.EncryptedGeneralUserDataPayload = userSnapshot.EncryptedGeneralUserDataPayload;
            user.EncryptedUserPasswordsDataPayload = userSnapshot.EncryptedUserPasswordsDataPayload;
            user.EncryptedUserDevicesDataPayload = userSnapshot.EncryptedUserDevicesDataPayload;
            user.SavedKey = null;
            user.LastModifiedAt = UtcDateTimeUtil.ToUtc(userSnapshot.LastModifiedAt == default ? now : userSnapshot.LastModifiedAt);
            user.UserDataLastModifiedAt = UtcDateTimeUtil.ToUtc(userSnapshot.UserDataLastModifiedAt == default ? user.LastModifiedAt : userSnapshot.UserDataLastModifiedAt);
            user.GeneralUserDataLastModifiedAt = UtcDateTimeUtil.ToUtc(userSnapshot.GeneralUserDataLastModifiedAt == default ? user.LastModifiedAt : userSnapshot.GeneralUserDataLastModifiedAt);
            user.UserPasswordsDataLastModifiedAt = UtcDateTimeUtil.ToUtc(userSnapshot.UserPasswordsDataLastModifiedAt == default ? user.LastModifiedAt : userSnapshot.UserPasswordsDataLastModifiedAt);
            user.UserDevicesDataLastModifiedAt = UtcDateTimeUtil.ToUtc(userSnapshot.UserDevicesDataLastModifiedAt == default ? user.LastModifiedAt : userSnapshot.UserDevicesDataLastModifiedAt);
            user.GenerateIntegrityHash();
            if (!Hashing.Verify(userSnapshot.IntegrityHash, user.IntegrityHash))
                throw new InvalidDataException("The enrollment snapshot contains invalid user integrity data.");
        }

        foreach (var groupSnapshot in snapshot.Groups)
        {
            var group = await groups.GetByIdWithUsersAsync(groupSnapshot.Id, ct);
            if (group is null)
            {
                group = new Group { Id = groupSnapshot.Id };
                await groups.AddAsync(group, ct);
            }

            group.EncryptedPayload = groupSnapshot.EncryptedPayload;
            group.LastModifiedAt = UtcDateTimeUtil.ToUtc(groupSnapshot.LastModifiedAt == default ? now : groupSnapshot.LastModifiedAt);
            group.IntegrityHash = groupSnapshot.IntegrityHash;
        }

        await unitOfWork.SaveChangesAsync(ct);

        foreach (var groupSnapshot in snapshot.Groups)
        {
            var group = await groups.GetByIdWithUsersAsync(groupSnapshot.Id, ct)
                ?? throw new InvalidDataException("The enrollment snapshot group could not be persisted.");
            var userIds = groupSnapshot.UserIds.Where(id => id != Guid.Empty).Distinct().ToHashSet();

            foreach (var user in group.Users.Where(u => !userIds.Contains(u.UId)).ToList())
                group.Users.Remove(user);

            foreach (var userId in userIds)
            {
                if (group.Users.Any(u => u.UId == userId))
                    continue;

                var user = await users.GetByIdAsync(userId, ct);
                if (user is not null)
                    group.Users.Add(user);
            }
        }

        var linkSnapshots = snapshot.UserDevices
            .Where(ud => ud.UserId != Guid.Empty && ud.DeviceId != Guid.Empty)
            .GroupBy(ud => new { ud.UserId, ud.DeviceId })
            .Select(group => group.OrderByDescending(ud => ud.LastModifiedAt).First())
            .ToList();

        foreach (var linkSnapshot in linkSnapshots)
        {
            if (linkSnapshot.LastModifiedAt == default || linkSnapshot.IntegrityHash.Length != Hashing.SHA256HashSizeInBytes)
                throw new InvalidDataException("The enrollment snapshot contains an incomplete user-device relationship.");

            var verifiedSnapshotLink = new UserDevice
            {
                UserId = linkSnapshot.UserId,
                DeviceId = linkSnapshot.DeviceId,
                IsSyncOn = linkSnapshot.IsSyncOn,
                IsDeleted = linkSnapshot.IsDeleted,
                DeletedAt = UtcDateTimeUtil.ToUtc(linkSnapshot.DeletedAt),
                LastModifiedAt = UtcDateTimeUtil.ToUtc(linkSnapshot.LastModifiedAt)
            };
            verifiedSnapshotLink.GenerateIntegrityHash();
            if (!Hashing.Verify(linkSnapshot.IntegrityHash, verifiedSnapshotLink.IntegrityHash))
                throw new InvalidDataException("The enrollment snapshot contains an invalid user-device relationship hash.");

            if (linkSnapshot.DeviceId == _identity.LocalDeviceId)
                continue;

            var remoteDevice = await devices.GetByIdAsync(linkSnapshot.DeviceId, ct);
            if (remoteDevice is null)
                continue;

            var link = await GetOrCreateUserDeviceAsync(userDevices, linkSnapshot.UserId, linkSnapshot.DeviceId, ct);
            link.Device = remoteDevice;
            link.IsSyncOn = linkSnapshot.IsSyncOn;
            link.IsDeleted = linkSnapshot.IsDeleted;
            link.DeletedAt = UtcDateTimeUtil.ToUtc(linkSnapshot.DeletedAt);
            link.LastModifiedAt = UtcDateTimeUtil.ToUtc(linkSnapshot.LastModifiedAt);
            link.IntegrityHash = verifiedSnapshotLink.IntegrityHash.ToArray();
        }

        await _localLinks.EnsureLocalUserDeviceAsync(devices, localUserDevices, snapshot.PrimaryUserId, ct);

        await unitOfWork.SaveChangesAsync(ct);
        await _localLinks.RemoveLocalDeviceRowsAsync(devices, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var trustedDevices = await devices.ListTrustedUnblockedAsync(ct);
        foreach (var device in trustedDevices)
            syncIdentities.TryAdd(device);
    }


    private async Task<Device?> FindExistingDeviceForSnapshotAsync(IDeviceRepository devices, DeviceEnrollmentDeviceSnapshot snapshot, CancellationToken ct)
    {
        var matches = new List<Device>();

        var byId = await devices.GetByIdAsync(snapshot.Id, ct);
        if (byId is not null)
            matches.Add(byId);

        var byFingerprint = await devices.GetByTlsCertFingerprintAsync(snapshot.TlsCertFingerprint, ct);
        if (byFingerprint is not null)
            matches.Add(byFingerprint);

        var bySignPublicKey = await devices.GetBySignPublicKeyAsync(snapshot.SignPublicKey, ct);
        if (bySignPublicKey is not null)
            matches.Add(bySignPublicKey);

        var distinctMatches = matches
            .GroupBy(device => device.Id)
            .Select(group => group.First())
            .ToList();

        if (distinctMatches.Count == 0)
            return null;

        if (distinctMatches.Count > 1 || distinctMatches[0].Id != snapshot.Id)
            throw new InvalidDataException("The enrollment snapshot contains conflicting duplicate device identity data.");

        var device = distinctMatches[0];
        if (!device.SignPublicKey.SequenceEqual(snapshot.SignPublicKey) ||
            !device.PublicKey.SequenceEqual(snapshot.PublicKey) ||
            !string.Equals(FingerprintUtil.Normalize(device.TlsCertFingerprint), FingerprintUtil.Normalize(snapshot.TlsCertFingerprint), StringComparison.OrdinalIgnoreCase) ||
            device.DeviceType != snapshot.DeviceType)
            throw new InvalidDataException("The enrollment snapshot contains conflicting duplicate device identity data.");

        return device;
    }


    private async Task<UserDevice> GetOrCreateUserDeviceAsync(
        IUserDeviceRepository userDevices,
        Guid userId,
        Guid deviceId,
        CancellationToken ct)
    {
        var existing = await userDevices.GetAsync(userId, deviceId, ct);
        if (existing is not null)
        {
            existing.VerifyIntegrity();
            return existing;
        }

        var created = new UserDevice
        {
            UserId = userId,
            DeviceId = deviceId
        };

        await userDevices.AddAsync(created, ct);
        return created;
    }






}
