using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using System.Security.Cryptography;
using System.Text.Json;
using static PasswordManagerLocal.Backend.Utils.DataCodec;

namespace PasswordManagerLocal.Backend.Services;

public sealed class UserDataBundleSyncService : IUserDataBundleSyncService
{
    private readonly IUserRepository _users;
    private readonly IUserDataBundleIntegrityService _integrity;
    private readonly IUserPasswordsDataMergeService _passwordsDataMerge;
    private readonly IUserDevicesDataMergeService _devicesDataMerge;
    private readonly ISyncVersionClockService _versionClock;

    public UserDataBundleSyncService(
        IUserRepository users,
        IUserDataBundleIntegrityService integrity,
        IUserPasswordsDataMergeService passwordsDataMerge,
        IUserDevicesDataMergeService devicesDataMerge,
        ISyncVersionClockService versionClock)
    {
        _users = users;
        _integrity = integrity;
        _passwordsDataMerge = passwordsDataMerge;
        _devicesDataMerge = devicesDataMerge;
        _versionClock = versionClock;
    }

    public async Task<UserSnapshotMergeBatchResult> TryVerifyAndMergeManyAsync(
        User existing,
        IReadOnlyList<UserSnapshotEnvelope> snapshots,
        EncryptionKey key,
        CancellationToken ct = default)
    {
        if (snapshots.Count == 0)
            return new UserSnapshotMergeBatchResult(false, []);

        var ordered = snapshots
            .OrderBy(snapshot => snapshot.OriginDeviceId)
            .ThenBy(snapshot => snapshot.OriginInstanceId)
            .ThenBy(snapshot => snapshot.OriginRevision)
            .ToArray();

        var canonicalBundle = await ReadAndVerifyUserDataBundleForSyncAsync(existing, key, ct);
        var incomingBundles = new List<UserDataBundle>(ordered.Length);
        var results = new List<UserSnapshotMergeEntryResult>(ordered.Length);
        var changedBlobs = UserDataBlobKind.None;
        var anyVerified = false;

        try
        {
            foreach (var snapshot in ordered)
            {
                ct.ThrowIfCancellationRequested();

                if (!existing.PasswordSalt.SequenceEqual(snapshot.User.PasswordSalt))
                {
                    results.Add(Failed(snapshot, "The snapshot password salt does not match the active key epoch."));
                    continue;
                }

                UserDataBundle incomingBundle;
                try
                {
                    var incomingUser = CreateUser(snapshot.User);
                    CopyUserData(snapshot.User, incomingUser);
                    incomingUser.LastModifiedAt = FromTimestamp(snapshot.CreatedAtUtc.ToUnixTimeMilliseconds());
                    incomingUser.GenerateIntegrityHash();
                    incomingBundle = await ReadAndVerifyUserDataBundleForSyncAsync(incomingUser, key, ct);
                }
                catch (Exception ex) when (IsSnapshotVerificationFailure(ex))
                {
                    results.Add(Failed(snapshot, ex.Message));
                    continue;
                }

                incomingBundles.Add(incomingBundle);
                _versionClock.Observe(SyncVersionStampTraversal.Enumerate(incomingBundle));
                anyVerified = true;
                var snapshotChangedBlobs = UserDataBlobKind.None;

                try
                {
                    if (GeneralUserDataMergeUtil.Merge(
                            canonicalBundle.GeneralUserData,
                            incomingBundle.GeneralUserData,
                            existing,
                            snapshot.User))
                        snapshotChangedBlobs |= UserDataBlobKind.General;
                    if (_passwordsDataMerge.Merge(canonicalBundle.UserPasswordsData, incomingBundle.UserPasswordsData))
                        snapshotChangedBlobs |= UserDataBlobKind.Passwords;
                    if (_devicesDataMerge.Merge(canonicalBundle.UserDevicesData, incomingBundle.UserDevicesData))
                        snapshotChangedBlobs |= UserDataBlobKind.Devices;
                }
                catch (DeterministicSyncConflictException ex) when (!ex.UserId.HasValue)
                {
                    throw ex.WithUserId(existing.UId);
                }

                changedBlobs |= snapshotChangedBlobs;
                var snapshotTimestamp = snapshot.CreatedAtUtc.ToUniversalTime();
                if (snapshotChangedBlobs != UserDataBlobKind.None)
                {
                    existing.UserDataLastModifiedAt = MaxDateTimeOffset(
                        existing.UserDataLastModifiedAt,
                        snapshot.User.UserDataLastModifiedAt,
                        snapshotTimestamp);
                }
                if (snapshotChangedBlobs.HasFlag(UserDataBlobKind.General))
                {
                    existing.GeneralUserDataLastModifiedAt = MaxDateTimeOffset(
                        existing.GeneralUserDataLastModifiedAt,
                        snapshot.User.GeneralUserDataLastModifiedAt,
                        snapshotTimestamp);
                }
                if (snapshotChangedBlobs.HasFlag(UserDataBlobKind.Passwords))
                {
                    existing.UserPasswordsDataLastModifiedAt = MaxDateTimeOffset(
                        existing.UserPasswordsDataLastModifiedAt,
                        snapshot.User.UserPasswordsDataLastModifiedAt,
                        snapshotTimestamp);
                }
                if (snapshotChangedBlobs.HasFlag(UserDataBlobKind.Devices))
                {
                    existing.UserDevicesDataLastModifiedAt = MaxDateTimeOffset(
                        existing.UserDevicesDataLastModifiedAt,
                        snapshot.User.UserDevicesDataLastModifiedAt,
                        snapshotTimestamp);
                }

                existing.LastModifiedAt = MaxDateTimeOffset(existing.LastModifiedAt, snapshotTimestamp);
                results.Add(new UserSnapshotMergeEntryResult(
                    snapshot.OriginDeviceId,
                    snapshot.OriginInstanceId,
                    snapshot.OriginRevision,
                    true));
            }

            if (!anyVerified)
                return new UserSnapshotMergeBatchResult(false, results);

            if (changedBlobs != UserDataBlobKind.None)
                await PersistMergedUserBundleAsync(existing, canonicalBundle, key, changedBlobs, ct);

            existing.GenerateIntegrityHash();
            _users.Update(existing);
            return new UserSnapshotMergeBatchResult(changedBlobs != UserDataBlobKind.None, results);
        }
        finally
        {
            // Merge services may move item references from an incoming bundle into the canonical bundle.
            // Dispose canonical first; child disposals are idempotent when incoming bundles are disposed next.
            canonicalBundle.Dispose();
            foreach (var incomingBundle in incomingBundles)
                incomingBundle.Dispose();
        }
    }

    private static UserSnapshotMergeEntryResult Failed(UserSnapshotEnvelope snapshot, string reason) =>
        new(
            snapshot.OriginDeviceId,
            snapshot.OriginInstanceId,
            snapshot.OriginRevision,
            false,
            reason.Length <= 512 ? reason : reason[..512]);

    private static bool IsSnapshotVerificationFailure(Exception ex) =>
        ex is UnauthorizedAccessException or
            CryptographicException or
            InvalidDataException or
            InvalidDataIntegrityException or
            JsonException;


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

        return max;
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
