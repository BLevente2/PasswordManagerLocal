using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Constants;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync.Enrollment;
using PasswordManagerLocal.Backend.Utils;
using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Responses;
using PasswordManagerLocal.Backend.Sync;
using System.Net;
using System.Net.Sockets;
using static PasswordManagerLocal.Backend.Constants.SyncConstants;
using PasswordManagerLocal.Backend.Abstractions.Caching;
using PasswordManagerLocal.Backend.State;

namespace PasswordManagerLocal.Backend.Services;

public sealed class DeviceEnrollmentSnapshotService : IDeviceEnrollmentSnapshotService
{
    private readonly IDeviceIdentityService _identity;

    private static readonly string[] SensitiveLocalOnlySnapshotPropertyNames =
    [
        "SavedKey",
        "LocalDeviceIdentity",
        "LocalUserDevice",
        "LocalUserDevices",
        "DeviceIdentity",
        "AgreementPrivateKeyBlob",
        "SignPrivateKeyBlob",
        "PFXCertificate",
        "PrivateKey",
        "PrivateKeyBlob"
    ];

    public DeviceEnrollmentSnapshotService(IDeviceIdentityService identity)
    {
        _identity = identity;
    }

    public DeviceEnrollmentSnapshot DecryptAndDeserialize(
        string sessionId,
        byte[] secret,
        byte[] ciphertext,
        string sourceDeviceId,
        byte[] sourceSignPublicKey,
        string sourceTlsFingerprint,
        int encryptionVersion,
        byte[] nonce,
        byte[] tag)
    {
        var plaintextSnapshotBytes = DecryptEnrollmentSnapshot(
            sessionId,
            secret,
            ciphertext,
            sourceDeviceId,
            sourceSignPublicKey,
            sourceTlsFingerprint,
            encryptionVersion,
            nonce,
            tag);

        try
        {
            RejectSensitiveLocalOnlySnapshotPayload(plaintextSnapshotBytes);
            var snapshot = JsonSerializer.Deserialize(
                plaintextSnapshotBytes,
                BackendJsonSerializerContext.Default.DeviceEnrollmentSnapshot);
            if (snapshot is null || snapshot.PrimaryUserId == Guid.Empty)
                throw new InvalidDataException("The received profile data is empty.");

            return UtcDateTimeUtil.NormalizeObjectGraph(snapshot);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The received profile data is invalid: {ex.Message}", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextSnapshotBytes);
        }
    }

    public async Task<DeviceEnrollmentSnapshot> BuildAsync(IServiceProvider services, Guid userId, CancellationToken ct = default)
    {
        var users = services.GetRequiredService<IUserRepository>();
        var userDevicesRepository = services.GetRequiredService<IUserDeviceRepository>();
        var groupsRepository = services.GetRequiredService<IGroupRepository>();
        var devicesRepository = services.GetRequiredService<IDeviceRepository>();

        var user = await users.GetByIdAsNoTrackingWithRelationsAsync(userId, ct);
        if (user is null)
            throw new UserNotFoundException();

        var allUserDevices = await userDevicesRepository.ListByUserAsync(userId, ct);
        foreach (var userDevice in allUserDevices)
            userDevice.VerifyIntegrity();

        var userDevices = allUserDevices.Where(ud => !ud.IsDeleted).ToList();
        var userDeviceIds = userDevices.Select(ud => ud.DeviceId).Distinct().ToList();
        var groups = await groupsRepository.ListByUserWithUserIdsAsNoTrackingAsync(userId, ct);
        var devices = await devicesRepository.ListByIdsWithUserDevicesAsNoTrackingAsync(userDeviceIds, _identity.LocalDeviceId, ct);

        foreach (var device in devices)
        {
            foreach (var link in device.UserDevices)
                link.VerifyIntegrity();
            device.GenerateIntegrityHash();
        }
        foreach (var group in groups)
        {
            var integritySource = new Group
            {
                Id = group.Id,
                EncryptedPayload = group.EncryptedPayload,
                LastModifiedAt = group.LastModifiedAt
            };
            integritySource.GenerateIntegrityHash();
            group.IntegrityHash = integritySource.IntegrityHash;
        }
        user.GenerateIntegrityHash();

        var deviceSnapshots = devices.Select(d => new DeviceEnrollmentDeviceSnapshot
        {
            Id = d.Id,
            PublicKey = d.PublicKey,
            SignPublicKey = d.SignPublicKey,
            TlsCertFingerprint = d.TlsCertFingerprint,
            DeviceType = d.DeviceType,
            LastKnownHash = d.LastKnownHash,
            LastSync = UtcDateTimeUtil.ToUtc(d.LastSync),
            LastSeen = UtcDateTimeUtil.ToUtc(d.LastSeen),
            IsTrusted = d.IsTrusted,
            IsBlocked = d.IsBlocked,
            BlockedReason = d.BlockedReason,
            BlockedAt = UtcDateTimeUtil.ToUtc(d.BlockedAt),
            InvalidSyncAttemptCount = d.InvalidSyncAttemptCount,
            LastInvalidSyncAttemptAt = UtcDateTimeUtil.ToUtc(d.LastInvalidSyncAttemptAt),
            LastModifiedAt = UtcDateTimeUtil.ToUtc(d.LastModifiedAt),
            IntegrityHash = d.IntegrityHash,
            UserIds = d.UserDevices.Where(ud => !ud.IsDeleted).Select(ud => ud.UserId).Distinct().ToList()
        }).ToList();

        var now = DateTimeOffset.UtcNow;
        var localDevice = new Device
        {
            Id = _identity.LocalDeviceId,
            PublicKey = _identity.AgreementPublicKey,
            SignPublicKey = _identity.SignPublicKey,
            TlsCertFingerprint = _identity.FingerprintHex,
            DeviceType = _identity.DeviceType,
            LastSync = now.UtcDateTime,
            LastSeen = now.UtcDateTime,
            IsTrusted = true,
            IsBlocked = false,
            LastModifiedAt = now
        };
        localDevice.GenerateIntegrityHash();
        var localUserDeviceSnapshotSource = new UserDevice
        {
            UserId = userId,
            DeviceId = _identity.LocalDeviceId,
            IsSyncOn = true,
            IsDeleted = false,
            LastModifiedAt = now
        };
        localUserDeviceSnapshotSource.GenerateIntegrityHash();

        deviceSnapshots.Add(new DeviceEnrollmentDeviceSnapshot
        {
            Id = localDevice.Id,
            PublicKey = localDevice.PublicKey,
            SignPublicKey = localDevice.SignPublicKey,
            TlsCertFingerprint = localDevice.TlsCertFingerprint,
            DeviceType = localDevice.DeviceType,
            LastKnownHash = localDevice.LastKnownHash,
            LastSync = UtcDateTimeUtil.ToUtc(localDevice.LastSync),
            LastSeen = UtcDateTimeUtil.ToUtc(localDevice.LastSeen),
            IsTrusted = true,
            IsBlocked = false,
            LastModifiedAt = UtcDateTimeUtil.ToUtc(localDevice.LastModifiedAt),
            IntegrityHash = localDevice.IntegrityHash,
            UserIds = [userId]
        });

        return new DeviceEnrollmentSnapshot
        {
            PrimaryUserId = user.UId,
            Users =
            [
                new DeviceEnrollmentUserSnapshot
                {
                    UId = user.UId,
                    UsernameHash = user.UsernameHash,
                    UsernameSalt = user.UsernameSalt,
                    PasswordSalt = user.PasswordSalt,
                    EncryptedPayload = user.EncryptedPayload,
                    EncryptedGeneralUserDataPayload = user.EncryptedGeneralUserDataPayload,
                    EncryptedUserPasswordsDataPayload = user.EncryptedUserPasswordsDataPayload,
                    EncryptedUserDevicesDataPayload = user.EncryptedUserDevicesDataPayload,
                    LastModifiedAt = UtcDateTimeUtil.ToUtc(user.LastModifiedAt),
                    UserDataLastModifiedAt = UtcDateTimeUtil.ToUtc(user.UserDataLastModifiedAt),
                    GeneralUserDataLastModifiedAt = UtcDateTimeUtil.ToUtc(user.GeneralUserDataLastModifiedAt),
                    UserPasswordsDataLastModifiedAt = UtcDateTimeUtil.ToUtc(user.UserPasswordsDataLastModifiedAt),
                    UserDevicesDataLastModifiedAt = UtcDateTimeUtil.ToUtc(user.UserDevicesDataLastModifiedAt),
                    IntegrityHash = user.IntegrityHash,
                    GroupIds = user.Groups.Select(g => g.Id).Distinct().ToList()
                }
            ],
            Groups = groups.Select(g => new DeviceEnrollmentGroupSnapshot
            {
                Id = g.Id,
                EncryptedPayload = g.EncryptedPayload,
                LastModifiedAt = UtcDateTimeUtil.ToUtc(g.LastModifiedAt),
                IntegrityHash = g.IntegrityHash,
                UserIds = g.UserIds
            }).ToList(),
            Devices = deviceSnapshots,
            UserDevices = userDevices.Select(ud => new DeviceEnrollmentUserDeviceSnapshot
            {
                UserId = ud.UserId,
                DeviceId = ud.DeviceId,
                IsSyncOn = ud.IsSyncOn,
                IsDeleted = ud.IsDeleted,
                DeletedAt = UtcDateTimeUtil.ToUtc(ud.DeletedAt),
                LastModifiedAt = UtcDateTimeUtil.ToUtc(ud.LastModifiedAt),
                IntegrityHash = ud.IntegrityHash.ToArray()
            }).Append(new DeviceEnrollmentUserDeviceSnapshot
            {
                UserId = localUserDeviceSnapshotSource.UserId,
                DeviceId = localUserDeviceSnapshotSource.DeviceId,
                IsSyncOn = localUserDeviceSnapshotSource.IsSyncOn,
                IsDeleted = localUserDeviceSnapshotSource.IsDeleted,
                DeletedAt = UtcDateTimeUtil.ToUtc(localUserDeviceSnapshotSource.DeletedAt),
                LastModifiedAt = UtcDateTimeUtil.ToUtc(localUserDeviceSnapshotSource.LastModifiedAt),
                IntegrityHash = localUserDeviceSnapshotSource.IntegrityHash.ToArray()
            }).ToList()
        };
    }


    public async Task EnsureEncryptedDeviceDataAsync(IUserService users, User user, Guid token, Guid deviceId, CancellationToken ct = default)
    {
        using var bundle = await users.GetAndVerifyUserDataBundleAsync(user, token, ct);
        if (bundle.UserDevicesData.Devices.Any(device => device.Id == deviceId))
            return;

        var baseName = DeviceNameUtil.BuildDefaultDeviceName(deviceId);
        var name = BuildUniqueEncryptedDeviceName(bundle.UserDevicesData, baseName, deviceId);
        var deviceData = new UserDeviceData
        {
            Id = deviceId,
            Name = name,
            LinkedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow
        };
        deviceData.GenerateIntegrityHash();
        bundle.UserDevicesData.DeletedDevices.RemoveAll(deleted => deleted.Id == deviceData.Id);
        bundle.UserDevicesData.Devices.Add(deviceData);
        await users.UpdateUserDataBundleAsync(bundle, token, UserDataBlobKind.Devices, false, ct);
    }


    private string BuildUniqueEncryptedDeviceName(UserDevicesData userDevicesData, string requestedName, Guid deviceId)
    {
        var baseName = string.IsNullOrWhiteSpace(requestedName)
            ? DeviceNameUtil.BuildDefaultDeviceName(deviceId)
            : requestedName.Trim();

        bool IsTaken(string value) => userDevicesData.Devices.Any(device =>
            device.Id != deviceId && string.Equals(device.Name, value, StringComparison.OrdinalIgnoreCase));

        if (!IsTaken(baseName))
            return baseName;

        for (var i = 2; i < 100; i++)
        {
            var suffix = $"-{i}";
            var prefixLength = Math.Min(baseName.Length, 64 - suffix.Length);
            var candidate = baseName[..prefixLength] + suffix;
            if (!IsTaken(candidate))
                return candidate;
        }

        throw new InvalidInputException();
    }


    public (byte[] Ciphertext, byte[] Nonce, byte[] Tag) Encrypt(
        string sessionId,
        byte[] secret,
        byte[] plaintext,
        string sourceDeviceId,
        byte[] sourceSignPublicKey,
        string sourceTlsFingerprint)
    {
        var key = DeviceEnrollmentCode.BuildSnapshotEncryptionKey(sessionId, secret);
        var nonce = RandomNumberGenerator.GetBytes(SyncConstants.EnrollmentSnapshotEncryptionNonceBytes);
        var tag = new byte[SyncConstants.EnrollmentSnapshotEncryptionTagBytes];
        var ciphertext = new byte[plaintext.Length];
        var aad = DeviceEnrollmentCode.BuildSnapshotEncryptionAad(sessionId, sourceDeviceId, sourceSignPublicKey, sourceTlsFingerprint);

        using var aes = new AesGcm(key, SyncConstants.EnrollmentSnapshotEncryptionTagBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);

        CryptographicOperations.ZeroMemory(key);
        return (ciphertext, nonce, tag);
    }


    private byte[] DecryptEnrollmentSnapshot(
        string sessionId,
        byte[] secret,
        byte[] ciphertext,
        string sourceDeviceId,
        byte[] sourceSignPublicKey,
        string sourceTlsFingerprint,
        int encryptionVersion,
        byte[] nonce,
        byte[] tag)
    {
        if (encryptionVersion != SyncConstants.EnrollmentSnapshotEncryptionVersion)
            throw new InvalidDataException("The enrollment snapshot encryption version is invalid.");

        if (nonce.Length != SyncConstants.EnrollmentSnapshotEncryptionNonceBytes)
            throw new InvalidDataException("The enrollment snapshot encryption nonce is invalid.");

        if (tag.Length != SyncConstants.EnrollmentSnapshotEncryptionTagBytes)
            throw new InvalidDataException("The enrollment snapshot authentication tag is invalid.");

        var key = DeviceEnrollmentCode.BuildSnapshotEncryptionKey(sessionId, secret);
        var plaintext = new byte[ciphertext.Length];
        var aad = DeviceEnrollmentCode.BuildSnapshotEncryptionAad(sessionId, sourceDeviceId, sourceSignPublicKey, sourceTlsFingerprint);

        try
        {
            using var aes = new AesGcm(key, SyncConstants.EnrollmentSnapshotEncryptionTagBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);
            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }


    private void RejectSensitiveLocalOnlySnapshotPayload(byte[] payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            foreach (var propertyName in SensitiveLocalOnlySnapshotPropertyNames)
            {
                if (ContainsProperty(doc.RootElement, propertyName))
                    throw new InvalidDataException("Enrollment snapshot contains local-only device or key material.");
            }
        }
        catch (JsonException)
        {
            throw;
        }
    }


    private bool ContainsProperty(JsonElement element, string propertyName)
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
}
