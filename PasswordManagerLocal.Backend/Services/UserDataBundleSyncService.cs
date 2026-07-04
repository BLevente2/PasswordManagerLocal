using PasswordManagerLocal.Backend.Abstractions.Security;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using System.Security.Cryptography;
using static PasswordManagerLocal.Backend.Utils.DataCodec;
using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models.Projections;
using static PasswordManagerLocal.Backend.Constants.DataLengthConstants;
using System.Text.Json;

namespace PasswordManagerLocal.Backend.Services;

public sealed class UserDataBundleSyncService : IUserDataBundleSyncService
{
    private readonly IUserRepository _users;
    private readonly IAuthService _auth;
    private readonly IKeyProtector _keyProtector;
    private readonly IUserDataBundleIntegrityService _integrity;
    private readonly IUserPasswordsDataMergeService _passwordsDataMerge;
    private readonly IUserDevicesDataMergeService _devicesDataMerge;
    private readonly ISyncRelationshipReconciliationService _relationships;

    public UserDataBundleSyncService(
        IUserRepository users,
        IAuthService auth,
        IKeyProtector keyProtector,
        IUserDataBundleIntegrityService integrity,
        IUserPasswordsDataMergeService passwordsDataMerge,
        IUserDevicesDataMergeService devicesDataMerge,
        ISyncRelationshipReconciliationService relationships)
    {
        _users = users;
        _auth = auth;
        _keyProtector = keyProtector;
        _integrity = integrity;
        _passwordsDataMerge = passwordsDataMerge;
        _devicesDataMerge = devicesDataMerge;
        _relationships = relationships;
    }

    public async Task<bool> TryMergeAsync(User existing, UserSyncPayload incoming, long ts, CancellationToken ct)
    {
        var incomingTs = FromTimestamp(ts);

        if (!existing.PasswordSalt.SequenceEqual(incoming.PasswordSalt))
            return false;

        var incomingHasPotentiallyNewerEncryptedData =
            incoming.GeneralUserDataLastModifiedAt > existing.GeneralUserDataLastModifiedAt ||
            incoming.UserPasswordsDataLastModifiedAt > existing.UserPasswordsDataLastModifiedAt ||
            incoming.UserDevicesDataLastModifiedAt > existing.UserDevicesDataLastModifiedAt;
        var localHasPotentiallyNewerEncryptedData =
            existing.GeneralUserDataLastModifiedAt > incoming.GeneralUserDataLastModifiedAt ||
            existing.UserPasswordsDataLastModifiedAt > incoming.UserPasswordsDataLastModifiedAt ||
            existing.UserDevicesDataLastModifiedAt > incoming.UserDevicesDataLastModifiedAt;
        var relationUpdateIsNewer = incomingTs > existing.LastModifiedAt;
        var encryptedBlobsDiffer =
            !existing.EncryptedGeneralUserDataPayload.SequenceEqual(incoming.EncryptedGeneralUserDataPayload) ||
            !existing.EncryptedUserPasswordsDataPayload.SequenceEqual(incoming.EncryptedUserPasswordsDataPayload) ||
            !existing.EncryptedUserDevicesDataPayload.SequenceEqual(incoming.EncryptedUserDevicesDataPayload);

        if (!incomingHasPotentiallyNewerEncryptedData && !localHasPotentiallyNewerEncryptedData && !relationUpdateIsNewer && !encryptedBlobsDiffer)
            return false;

        if (!TryGetUserEncryptionKeyForSync(existing, out var userKey) || userKey is null)
            return false;

        try
        {
            var incomingUser = CreateUser(incoming);
            CopyUserData(incoming, incomingUser);
            incomingUser.LastModifiedAt = incomingTs;
            incomingUser.GenerateIntegrityHash();

            var existingBundle = await ReadAndVerifyUserDataBundleForSyncAsync(existing, userKey, ct);
            var incomingBundle = await ReadAndVerifyUserDataBundleForSyncAsync(incomingUser, userKey, ct);

            var changedBlobs = UserDataBlobKind.None;
            if (MergeGeneralUserDataForSync(existingBundle, incomingBundle, existing, incoming))
                changedBlobs |= UserDataBlobKind.General;

            if (_passwordsDataMerge.Merge(existingBundle.UserPasswordsData, incomingBundle.UserPasswordsData))
                changedBlobs |= UserDataBlobKind.Passwords;

            if (_devicesDataMerge.Merge(existingBundle.UserDevicesData, incomingBundle.UserDevicesData))
                changedBlobs |= UserDataBlobKind.Devices;

            if (changedBlobs != UserDataBlobKind.None)
            {
                await PersistMergedUserBundleAsync(existing, existingBundle, userKey, changedBlobs, ct);

                existing.UserDataLastModifiedAt = MaxDateTimeOffset(existing.UserDataLastModifiedAt, incoming.UserDataLastModifiedAt, incomingTs);
                if (changedBlobs.HasFlag(UserDataBlobKind.General))
                    existing.GeneralUserDataLastModifiedAt = MaxDateTimeOffset(existing.GeneralUserDataLastModifiedAt, incoming.GeneralUserDataLastModifiedAt, incomingTs);
                if (changedBlobs.HasFlag(UserDataBlobKind.Passwords))
                    existing.UserPasswordsDataLastModifiedAt = MaxDateTimeOffset(existing.UserPasswordsDataLastModifiedAt, incoming.UserPasswordsDataLastModifiedAt, incomingTs);
                if (changedBlobs.HasFlag(UserDataBlobKind.Devices))
                    existing.UserDevicesDataLastModifiedAt = MaxDateTimeOffset(existing.UserDevicesDataLastModifiedAt, incoming.UserDevicesDataLastModifiedAt, incomingTs);
            }

            if (relationUpdateIsNewer)
            {
                await _relationships.SyncUserGroupsAsync(existing, incoming.GroupIds, ct);
                await _relationships.SyncUserDevicesAsync(existing, incoming.DeviceIds, incomingTs, ct);
            }

            existing.LastModifiedAt = MaxDateTimeOffset(existing.LastModifiedAt, incomingTs);
            existing.GenerateIntegrityHash();
            _users.Update(existing);
            return changedBlobs != UserDataBlobKind.None || relationUpdateIsNewer;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            userKey.Dispose();
        }
    }


    private bool TryGetUserEncryptionKeyForSync(User user, out EncryptionKey? key)
    {
        if (_auth.TryGetActiveUserEncryptionKey(user.UId, out key) && key is not null)
            return true;

        key = null;
        if (user.SavedKey is null || user.SavedKey.Length == 0)
            return false;

        byte[]? rawKey = null;
        try
        {
            rawKey = _keyProtector.Unprotect(user.SavedKey);
            key = EncryptionKey.FromRaw(rawKey);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            key = null;
            return false;
        }
        finally
        {
            if (rawKey is not null)
                CryptographicOperations.ZeroMemory(rawKey);
        }
    }


    private bool MergeGeneralUserDataForSync(UserDataBundle local, UserDataBundle incoming, User existingUser, UserSyncPayload incomingUser)
    {
        var localUpdatedAt = local.GeneralUserData.LastUpdatedAt;
        var incomingUpdatedAt = incoming.GeneralUserData.LastUpdatedAt;
        if (incomingUpdatedAt <= localUpdatedAt)
            return false;

        local.GeneralUserData.Username = incoming.GeneralUserData.Username;
        local.GeneralUserData.FirstName = incoming.GeneralUserData.FirstName;
        local.GeneralUserData.LastName = incoming.GeneralUserData.LastName;
        local.GeneralUserData.Email = incoming.GeneralUserData.Email;
        local.GeneralUserData.RegistrationDate = incoming.GeneralUserData.RegistrationDate;
        local.GeneralUserData.LastUpdatedAt = incoming.GeneralUserData.LastUpdatedAt;

        CryptographicOperations.ZeroMemory(existingUser.UsernameHash);
        CryptographicOperations.ZeroMemory(existingUser.UsernameSalt);
        existingUser.UsernameHash = incomingUser.UsernameHash.ToArray();
        existingUser.UsernameSalt = incomingUser.UsernameSalt.ToArray();
        return true;
    }


    private async Task<UserDataBundle> ReadAndVerifyUserDataBundleForSyncAsync(User user, EncryptionKey userKey, CancellationToken ct)
    {
        var userData = await DecryptDecompressDeserializeAsync(user.EncryptedPayload, userKey, BackendJsonSerializerContext.Default.UserData, ct: ct);
        if (userData is null)
            throw new UnauthorizedAccessException();

        try
        {
            _integrity.VerifyUserData(userData);
        }
        catch
        {
            userData.Dispose();
            throw;
        }

        var generalTask = DecryptAndVerifyEncryptedUserBlobAsync(
            user.EncryptedGeneralUserDataPayload,
            userData.GeneralUserDataKey,
            BackendJsonSerializerContext.Default.GeneralUserData,
            _integrity.VerifyGeneralUserData,
            ct);
        var passwordsTask = DecryptAndVerifyEncryptedUserBlobAsync(
            user.EncryptedUserPasswordsDataPayload,
            userData.UserPasswordsDataKey,
            BackendJsonSerializerContext.Default.UserPasswordsData,
            _integrity.VerifyUserPasswordsData,
            ct);
        var devicesTask = DecryptAndVerifyEncryptedUserBlobAsync(
            user.EncryptedUserDevicesDataPayload,
            userData.UserDevicesDataKey,
            BackendJsonSerializerContext.Default.UserDevicesData,
            _integrity.VerifyUserDevicesData,
            ct);

        try
        {
            await Task.WhenAll(generalTask, passwordsTask, devicesTask);

            var bundle = new UserDataBundle
            {
                UserData = userData,
                GeneralUserData = await generalTask,
                UserPasswordsData = await passwordsTask,
                UserDevicesData = await devicesTask
            };

            _integrity.VerifyBundleLinks(bundle);
            return bundle;
        }
        catch
        {
            DisposeCompletedTaskResult(generalTask);
            DisposeCompletedTaskResult(passwordsTask);
            DisposeCompletedTaskResult(devicesTask);
            userData.Dispose();
            throw;
        }
    }


    private async Task<T> DecryptEncryptedUserBlobAsync<T>(byte[] encryptedBlob, byte[] rawKey, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, CancellationToken ct) where T : class
    {
        if (encryptedBlob.Length == 0 || rawKey.Length == 0)
            throw new UnauthorizedAccessException();

        using var key = EncryptionKey.FromRaw(rawKey);
        var data = await DecryptDecompressDeserializeAsync(encryptedBlob, key, typeInfo, ct: ct);
        if (data is null)
            throw new UnauthorizedAccessException();

        return data;
    }


    private async Task<T> DecryptAndVerifyEncryptedUserBlobAsync<T>(
        byte[] encryptedBlob,
        byte[] rawKey,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        Action<T> verifyIntegrity,
        CancellationToken ct) where T : class, IDisposable
    {
        var data = await DecryptEncryptedUserBlobAsync(encryptedBlob, rawKey, typeInfo, ct);
        try
        {
            verifyIntegrity(data);
            return data;
        }
        catch
        {
            data.Dispose();
            throw;
        }
    }


    private async Task PersistMergedUserBundleAsync(
        User user,
        UserDataBundle bundle,
        EncryptionKey userKey,
        UserDataBlobKind changedBlobs,
        CancellationToken ct)
    {
        _integrity.RebuildModifiedBlobIntegrity(bundle, changedBlobs);

        Task<byte[]>? generalTask = changedBlobs.HasFlag(UserDataBlobKind.General)
            ? EncryptUserBlobAsync(bundle.GeneralUserData, bundle.UserData.GeneralUserDataKey, BackendJsonSerializerContext.Default.GeneralUserData, ct)
            : null;
        Task<byte[]>? passwordsTask = changedBlobs.HasFlag(UserDataBlobKind.Passwords)
            ? EncryptUserBlobAsync(bundle.UserPasswordsData, bundle.UserData.UserPasswordsDataKey, BackendJsonSerializerContext.Default.UserPasswordsData, ct)
            : null;
        Task<byte[]>? devicesTask = changedBlobs.HasFlag(UserDataBlobKind.Devices)
            ? EncryptUserBlobAsync(bundle.UserDevicesData, bundle.UserData.UserDevicesDataKey, BackendJsonSerializerContext.Default.UserDevicesData, ct)
            : null;
        var userDataTask = SerializeCompressEncryptAsync(bundle.UserData, userKey, BackendJsonSerializerContext.Default.UserData, ct: ct);

        var encryptionTasks = new List<Task<byte[]>> { userDataTask };
        if (generalTask is not null)
            encryptionTasks.Add(generalTask);
        if (passwordsTask is not null)
            encryptionTasks.Add(passwordsTask);
        if (devicesTask is not null)
            encryptionTasks.Add(devicesTask);

        try
        {
            await Task.WhenAll(encryptionTasks);
        }
        catch
        {
            foreach (var task in encryptionTasks)
                ZeroCompletedEncryptionTask(task);
            throw;
        }

        if (generalTask is not null)
            ReplaceEncryptedPayload(user.EncryptedGeneralUserDataPayload, await generalTask, value => user.EncryptedGeneralUserDataPayload = value);
        if (passwordsTask is not null)
            ReplaceEncryptedPayload(user.EncryptedUserPasswordsDataPayload, await passwordsTask, value => user.EncryptedUserPasswordsDataPayload = value);
        if (devicesTask is not null)
            ReplaceEncryptedPayload(user.EncryptedUserDevicesDataPayload, await devicesTask, value => user.EncryptedUserDevicesDataPayload = value);

        ReplaceEncryptedPayload(user.EncryptedPayload, await userDataTask, value => user.EncryptedPayload = value);
    }


    private async Task<byte[]> EncryptUserBlobAsync<T>(
        T data,
        byte[] rawKey,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken ct) where T : class
    {
        using var key = EncryptionKey.FromRaw(rawKey);
        return await SerializeCompressEncryptAsync(data, key, typeInfo, ct: ct);
    }


    private void DisposeCompletedTaskResult<T>(Task<T> task) where T : IDisposable
    {
        if (task.Status == TaskStatus.RanToCompletion)
            task.Result.Dispose();
    }


    private void ZeroCompletedEncryptionTask(Task<byte[]> task)
    {
        if (task.Status == TaskStatus.RanToCompletion)
            CryptographicOperations.ZeroMemory(task.Result);
    }


    private void ReplaceEncryptedPayload(byte[] currentPayload, byte[] replacementPayload, Action<byte[]> assignReplacement)
    {
        CryptographicOperations.ZeroMemory(currentPayload);
        assignReplacement(replacementPayload);
    }


    private DateTimeOffset MaxDateTimeOffset(params DateTimeOffset[] values)
    {
        var max = DateTimeOffset.MinValue;
        foreach (var value in values)
        {
            if (value != default && value > max)
                max = value;
        }

        return max == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : max;
    }


    private User CreateUser(UserSyncPayload payload) =>
        new()
        {
            UId = payload.UId
        };


    private void CopyUserData(UserSyncPayload source, User target)
    {
        target.UId = source.UId;
        target.UsernameHash = source.UsernameHash;
        target.UsernameSalt = source.UsernameSalt;
        target.PasswordSalt = source.PasswordSalt;
        target.EncryptedPayload = source.EncryptedPayload;
        target.EncryptedGeneralUserDataPayload = source.EncryptedGeneralUserDataPayload;
        target.EncryptedUserPasswordsDataPayload = source.EncryptedUserPasswordsDataPayload;
        target.EncryptedUserDevicesDataPayload = source.EncryptedUserDevicesDataPayload;
        target.UserDataLastModifiedAt = source.UserDataLastModifiedAt;
        target.GeneralUserDataLastModifiedAt = source.GeneralUserDataLastModifiedAt;
        target.UserPasswordsDataLastModifiedAt = source.UserPasswordsDataLastModifiedAt;
        target.UserDevicesDataLastModifiedAt = source.UserDevicesDataLastModifiedAt;
        target.IntegrityHash = source.IntegrityHash;
    }


    private DateTimeOffset FromTimestamp(long ts) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ts);
}
