using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using System.Security.Cryptography;
using static PasswordManagerLocal.Backend.Utils.DataCodec;
using static PasswordManagerLocal.Backend.Utils.DataValidationUtil;
using static PasswordManagerLocal.Backend.Constants.PasswordConstants;
using static PasswordManagerLocal.Backend.Constants.TombstoneConstants;

namespace PasswordManagerLocal.Backend.Services;

public sealed class UserService : IUserService
{
    private readonly IUserRepository _users;
    private readonly IDataCachingService _cache;
    private readonly IKeyVaultService _keys;
    private readonly ITokenService _tokens;
    private readonly ISyncQueueService _syncQueue;
    private readonly ISyncRuntimeService _syncRuntime;
    private readonly IUserDataBundleIntegrityService _integrity;
    private readonly IUnitOfWork _uow;

    public UserService(
        IUserRepository users,
        IDataCachingService cache,
        IKeyVaultService keys,
        ITokenService tokens,
        ISyncQueueService syncQueue,
        ISyncRuntimeService syncRuntime,
        IUserDataBundleIntegrityService integrity,
        IUnitOfWork uow)
    {
        _users = users;
        _cache = cache;
        _keys = keys;
        _tokens = tokens;
        _syncQueue = syncQueue;
        _syncRuntime = syncRuntime;
        _integrity = integrity;
        _uow = uow;
    }




    public Guid GetUidFromToken(Guid token)
    {
        if (!_tokens.TryGetUid(token, out var uid))
            throw new InvalidTokenException();
        return uid;
    }


    public EncryptionKey GetEncryptionKeyFromToken(Guid token)
    {
        if (!_keys.TryGetEncryptionKey(token, out var key))
            throw new InvalidTokenException();
        return key;
    }


    public Task<User?> GetUserByUidAsync(Guid uid, CancellationToken ct = default) =>
        _users.GetByIdAsync(uid, ct);


    public async Task<User> GetAndVerifyUserByUidAsync(Guid uid, CancellationToken ct = default)
    {
        var user = await GetUserByUidAsync(uid, ct);
        if (user is null)
            throw new UserNotFoundException();

        user.VerifyIntegrity();
        return user;
    }


    public async Task<User> GetAndVerifyUserAsync(Guid token, CancellationToken ct = default)
    {
        var uid = GetUidFromToken(token);
        return await GetAndVerifyUserByUidAsync(uid, ct);
    }


    public async Task<User?> GetUserByUsernameAsync(byte[] username, CancellationToken ct = default)
    {
        var lookupData = await _users.ListLoginLookupDataAsync(ct);

        foreach (var candidate in lookupData)
        {
            var calculatedHash = Hashing.SHA256Hash(username, candidate.UsernameSalt);
            try
            {
                if (!Hashing.Verify(candidate.UsernameHash, calculatedHash))
                    continue;

                return await _users.GetByIdAsync(candidate.UId, ct);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(calculatedHash);
            }
        }

        return null;
    }


    public async Task<User> GetAndVerifyUserByUsernameAsync(byte[] username, CancellationToken ct = default)
    {
        var user = await GetUserByUsernameAsync(username, ct);
        if (user is null)
            throw new UserNotFoundException();

        user.VerifyIntegrity();
        return user;
    }


    public async Task<UserData> GetAndVerifyUserDataAsync(User user, EncryptionKey key)
    {
        var userData = await DecryptUserDataAsync(user, key);
        try
        {
            _integrity.VerifyUserData(userData);
            return userData;
        }
        catch
        {
            userData.Dispose();
            throw;
        }
    }


    public async Task<UserData> GetAndVerifyUserDataAsync(User user, Guid token)
    {
        using var key = GetEncryptionKeyFromToken(token);
        return await GetAndVerifyUserDataAsync(user, key);
    }


    public async Task<UserDataBundle> GetAndVerifyUserDataBundleAsync(User user, EncryptionKey key, CancellationToken ct = default)
    {
        var userData = await DecryptUserDataAsync(user, key, ct);
        try
        {
            _integrity.VerifyUserData(userData);
        }
        catch
        {
            userData.Dispose();
            throw;
        }

        var generalTask = DecryptAndVerifyGeneralUserDataAsync(user, userData, ct);
        var passwordsTask = DecryptAndVerifyUserPasswordsDataAsync(user, userData, ct);
        var devicesTask = DecryptAndVerifyUserDevicesDataAsync(user, userData, ct);

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


    public async Task<UserDataBundle> GetAndVerifyUserDataBundleAsync(User user, Guid token, CancellationToken ct = default)
    {
        using var key = GetEncryptionKeyFromToken(token);
        var bundle = await GetAndVerifyUserDataBundleAsync(user, key, ct);
        _keys.SetUserBlobKeys(token, bundle.UserData);
        return bundle;
    }


    public bool TryGetAndVerifyUserDataFromCache(Guid token, out UserData? userData)
    {
        if (_cache.TryGetUserData(token, out var foundUserData) && foundUserData is not null)
        {
            _integrity.VerifyUserData(foundUserData);
            userData = foundUserData;
            return true;
        }

        userData = null;
        return false;
    }


    public bool TryGetAndVerifyUserDataBundleFromCache(Guid token, out UserDataBundle? bundle)
    {
        if (_cache.TryGetUserDataBundle(token, out var foundBundle) && foundBundle is not null)
        {
            _integrity.VerifyUntrustedBundle(foundBundle);
            bundle = foundBundle;
            return true;
        }

        bundle = null;
        return false;
    }


    public async Task<UserData> GetLoadAndVerifyUserDataAsync(Guid token, CancellationToken ct = default, User? user = null)
    {
        var bundle = await GetLoadAndVerifyUserDataBundleAsync(token, ct, user);
        return bundle.UserData;
    }


    public async Task<UserDataBundle> GetLoadAndVerifyUserDataBundleAsync(Guid token, CancellationToken ct = default, User? user = null)
    {
        if (TryGetAndVerifyUserDataBundleFromCache(token, out var foundBundle) && foundBundle is not null)
            return foundBundle;

        if (user is null)
            user = await GetAndVerifyUserAsync(token, ct);

        var bundle = await GetAndVerifyUserDataBundleAsync(user, token, ct);
        _cache.SetUserDataBundle(token, bundle);
        return bundle;
    }


    public async Task<IReadOnlyList<User>> GetAndVerifyRememberMeEnabledUsersAsync(CancellationToken ct = default)
    {
        var users = await _users.GetAllRememberMeEnabledUsersAsync(ct);
        var verifiedUsers = new List<User>(users.Count);

        foreach (var user in users)
        {
            user.VerifyIntegrity();
            verifiedUsers.Add(user);
        }

        return verifiedUsers;
    }


    public async Task AddNewUserAsync(User user, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        user.LastModifiedAt = now;
        EnsureBlobTimestamps(user, now);
        user.GenerateIntegrityHash();
        await _users.AddAsync(user, ct);
        await _uow.SaveChangesAsync(ct);
    }


    public Task UpdateUserAsync(User user, CancellationToken ct = default) =>
        UpdateUserAsync(user, false, ct);


    public async Task UpdateUserAsync(User user, bool enqueueSync, CancellationToken ct = default)
    {
        user.LastModifiedAt = DateTimeOffset.UtcNow;
        user.GenerateIntegrityHash();
        _users.Update(user);

        if (enqueueSync)
        {
            await _syncQueue.EnqueueAsync(new SyncItem
            {
                ModelId = user.UId,
                ModelType = SyncModelType.User,
                ChangeType = SyncChangeType.Updated
            }, ct);
            return;
        }

        await _uow.SaveChangesAsync(ct);
    }


    public Task UpdateUserDataAsync(UserData userData, User user, EncryptionKey key, CancellationToken ct = default) =>
        UpdateUserDataAsync(userData, user, key, false, ct);


    public async Task UpdateUserDataAsync(UserData userData, User user, EncryptionKey key, bool enqueueSync, CancellationToken ct = default)
    {
        EnsureUserDataCanBePersisted(userData, user);
        userData.GenerateIntegrityHash();
        var newEncryptedPayload = await SerializeCompressEncryptAsync(userData, key, BackendJsonSerializerContext.Default.UserData, ct: ct);
        CryptographicOperations.ZeroMemory(user.EncryptedPayload);
        user.EncryptedPayload = newEncryptedPayload;
        user.UserDataLastModifiedAt = DateTimeOffset.UtcNow;
        await UpdateUserAsync(user, enqueueSync, ct);
    }


    public Task UpdateUserDataAsync(UserData userData, Guid token, EncryptionKey key, CancellationToken ct = default) =>
        UpdateUserDataAsync(userData, token, key, false, ct);


    public async Task UpdateUserDataAsync(UserData userData, Guid token, EncryptionKey key, bool enqueueSync, CancellationToken ct = default)
    {
        var user = await GetAndVerifyUserAsync(token, ct);
        await UpdateUserDataAsync(userData, user, key, enqueueSync, ct);
    }


    public Task UpdateUserDataAsync(UserData userData, Guid token, CancellationToken ct = default) =>
        UpdateUserDataAsync(userData, token, false, ct);


    public async Task UpdateUserDataAsync(UserData userData, Guid token, bool enqueueSync, CancellationToken ct = default)
    {
        using var key = GetEncryptionKeyFromToken(token);
        await UpdateUserDataAsync(userData, token, key, enqueueSync, ct);
    }


    public Task UpdateUserDataBundleAsync(UserDataBundle bundle, User user, EncryptionKey key, UserDataBlobKind modifiedBlobs, CancellationToken ct = default) =>
        UpdateUserDataBundleAsync(bundle, user, key, modifiedBlobs, false, ct);


    public async Task UpdateUserDataBundleAsync(UserDataBundle bundle, User user, EncryptionKey key, UserDataBlobKind modifiedBlobs, bool enqueueSync, CancellationToken ct = default)
    {
        EnsureUserDataBundleCanBePersisted(bundle, user);
        await PersistUserDataBundleAsync(bundle, user, key, modifiedBlobs, false, enqueueSync, ct);
    }


    public Task UpdateUserDataBundleAsync(UserDataBundle bundle, Guid token, UserDataBlobKind modifiedBlobs, CancellationToken ct = default) =>
        UpdateUserDataBundleAsync(bundle, token, modifiedBlobs, false, ct);


    public async Task UpdateUserDataBundleAsync(UserDataBundle bundle, Guid token, UserDataBlobKind modifiedBlobs, bool enqueueSync, CancellationToken ct = default)
    {
        var user = await GetAndVerifyUserAsync(token, ct);
        using var key = GetEncryptionKeyFromToken(token);
        await UpdateUserDataBundleAsync(bundle, user, key, modifiedBlobs, enqueueSync, ct);
        _keys.SetUserBlobKeys(token, bundle.UserData);
        _cache.SetUserDataBundle(token, bundle);
    }


    public async Task ReencryptUserDataBundleWithNewKeysAsync(UserDataBundle bundle, User user, EncryptionKey newUserKey, bool enqueueSync, CancellationToken ct = default)
    {
        EnsureUserDataBundleCanBePersisted(bundle, user);
        _integrity.VerifyUntrustedBundle(bundle);
        ReplaceUserBlobKeys(bundle.UserData);
        await PersistUserDataBundleAsync(bundle, user, newUserKey, UserDataBlobKind.None, true, enqueueSync, ct);
    }


    private async Task PersistUserDataBundleAsync(UserDataBundle bundle, User user, EncryptionKey userKey, UserDataBlobKind modifiedBlobs, bool forceRewriteAllBlobs, bool enqueueSync, CancellationToken ct)
    {
        _integrity.UpdateModifiedBlobIntegrity(bundle, modifiedBlobs);
        EnsureUserDataBundleCanBePersisted(bundle, user);

        var rewriteGeneral = forceRewriteAllBlobs || modifiedBlobs.HasFlag(UserDataBlobKind.General);
        var rewritePasswords = forceRewriteAllBlobs || modifiedBlobs.HasFlag(UserDataBlobKind.Passwords);
        var rewriteDevices = forceRewriteAllBlobs || modifiedBlobs.HasFlag(UserDataBlobKind.Devices);

        Task<byte[]>? generalTask = rewriteGeneral
            ? EncryptBlobAsync(bundle.GeneralUserData, bundle.UserData.GeneralUserDataKey, BackendJsonSerializerContext.Default.GeneralUserData, ct)
            : null;
        Task<byte[]>? passwordsTask = rewritePasswords
            ? EncryptBlobAsync(bundle.UserPasswordsData, bundle.UserData.UserPasswordsDataKey, BackendJsonSerializerContext.Default.UserPasswordsData, ct)
            : null;
        Task<byte[]>? devicesTask = rewriteDevices
            ? EncryptBlobAsync(bundle.UserDevicesData, bundle.UserData.UserDevicesDataKey, BackendJsonSerializerContext.Default.UserDevicesData, ct)
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

        var now = DateTimeOffset.UtcNow;
        if (modifiedBlobs != UserDataBlobKind.None || forceRewriteAllBlobs)
            user.UserDataLastModifiedAt = now;

        if (generalTask is not null)
        {
            user.GeneralUserDataLastModifiedAt = now;
            ReplaceEncryptedPayload(user.EncryptedGeneralUserDataPayload, await generalTask, value => user.EncryptedGeneralUserDataPayload = value);
        }

        if (passwordsTask is not null)
        {
            user.UserPasswordsDataLastModifiedAt = now;
            ReplaceEncryptedPayload(user.EncryptedUserPasswordsDataPayload, await passwordsTask, value => user.EncryptedUserPasswordsDataPayload = value);
        }

        if (devicesTask is not null)
        {
            user.UserDevicesDataLastModifiedAt = now;
            ReplaceEncryptedPayload(user.EncryptedUserDevicesDataPayload, await devicesTask, value => user.EncryptedUserDevicesDataPayload = value);
        }

        ReplaceEncryptedPayload(user.EncryptedPayload, await userDataTask, value => user.EncryptedPayload = value);
        await UpdateUserAsync(user, enqueueSync, ct);
    }


    private async Task<UserData> DecryptUserDataAsync(User user, EncryptionKey key, CancellationToken ct = default)
    {
        var userData = await DecryptDecompressDeserializeAsync(user.EncryptedPayload, key, BackendJsonSerializerContext.Default.UserData, ct: ct);
        if (userData is null)
            throw new UnauthorizedAccessException();

        return userData;
    }


    private async Task<T> DecryptBlobAsync<T>(byte[] encryptedBlob, byte[] rawKey, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, CancellationToken ct) where T : class
    {
        if (encryptedBlob.Length == 0 || rawKey.Length == 0)
            throw new UnauthorizedAccessException();

        using var key = EncryptionKey.FromRaw(rawKey);
        var data = await DecryptDecompressDeserializeAsync(encryptedBlob, key, typeInfo, ct: ct);
        if (data is null)
            throw new UnauthorizedAccessException();

        return data;
    }


    private async Task<T> DecryptAndVerifyBlobAsync<T>(
        byte[] encryptedBlob,
        byte[] rawKey,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        Action<T> verifyIntegrity,
        CancellationToken ct) where T : class, IDisposable
    {
        var data = await DecryptBlobAsync(encryptedBlob, rawKey, typeInfo, ct);
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


    private Task<GeneralUserData> DecryptAndVerifyGeneralUserDataAsync(User user, UserData userData, CancellationToken ct) =>
        DecryptAndVerifyBlobAsync(
            user.EncryptedGeneralUserDataPayload,
            userData.GeneralUserDataKey,
            BackendJsonSerializerContext.Default.GeneralUserData,
            _integrity.VerifyGeneralUserData,
            ct);


    private Task<UserPasswordsData> DecryptAndVerifyUserPasswordsDataAsync(User user, UserData userData, CancellationToken ct) =>
        DecryptAndVerifyBlobAsync(
            user.EncryptedUserPasswordsDataPayload,
            userData.UserPasswordsDataKey,
            BackendJsonSerializerContext.Default.UserPasswordsData,
            _integrity.VerifyUserPasswordsData,
            ct);


    private Task<UserDevicesData> DecryptAndVerifyUserDevicesDataAsync(User user, UserData userData, CancellationToken ct) =>
        DecryptAndVerifyBlobAsync(
            user.EncryptedUserDevicesDataPayload,
            userData.UserDevicesDataKey,
            BackendJsonSerializerContext.Default.UserDevicesData,
            _integrity.VerifyUserDevicesData,
            ct);


    private async Task<byte[]> EncryptBlobAsync<T>(
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


    private void ReplaceUserBlobKeys(UserData userData) =>
        UserDataKeyUtil.ReplaceUserBlobKeys(userData);


    private void EnsureUserDataCanBePersisted(UserData userData, User user)
    {
        if (userData.UId == Guid.Empty || userData.UId != user.UId)
            throw new InvalidOperationException("Refusing to persist invalid user data.");

        if (userData.GeneralUserDataKey.Length == 0 ||
            userData.UserPasswordsDataKey.Length == 0 ||
            userData.UserDevicesDataKey.Length == 0 ||
            userData.GeneralUserDataIntegrityHash.Length != Hashing.SHA256HashSizeInBytes ||
            userData.UserPasswordsDataIntegrityHash.Length != Hashing.SHA256HashSizeInBytes ||
            userData.UserDevicesDataIntegrityHash.Length != Hashing.SHA256HashSizeInBytes)
            throw new InvalidOperationException("Refusing to persist incomplete user data.");
    }


    private void EnsureUserDataBundleCanBePersisted(UserDataBundle bundle, User user)
    {
        EnsureUserDataCanBePersisted(bundle.UserData, user);
        EnsurePasswordDataCanBePersisted(bundle.UserPasswordsData);
        EnsureUserDeviceDataCanBePersisted(bundle.UserDevicesData);
        EnsureCustomColorsCanBePersisted(bundle.UserPasswordsData);
        EnsurePasswordTagsCanBePersisted(bundle.UserPasswordsData);
        EnsurePasswordTagReferencesCanBePersisted(bundle.UserPasswordsData);
    }


    private void EnsurePasswordDataCanBePersisted(UserPasswordsData passwordsData)
    {
        if (passwordsData.PasswordKey.Length == 0)
            throw new InvalidOperationException("Refusing to persist incomplete user data.");

        if (passwordsData.Passwords.Count > MaxNumberOfPasswords)
            throw new InvalidOperationException("Refusing to persist too many passwords.");

        if (passwordsData.CustomColors.Count > MaxNumberOfCustomUserColors)
            throw new InvalidOperationException("Refusing to persist too many custom colors.");

        if (passwordsData.Tags.Count > MaxNumberOfPasswordTags)
            throw new InvalidOperationException("Refusing to persist too many password tags.");

        if (passwordsData.DeletedPasswords.Count > MaxUserDataTombstonesPerList ||
            passwordsData.DeletedCustomColors.Count > MaxUserDataTombstonesPerList ||
            passwordsData.DeletedTags.Count > MaxUserDataTombstonesPerList)
            throw new InvalidOperationException("Refusing to persist too many user data tombstones.");
    }


    private void EnsureUserDeviceDataCanBePersisted(UserDevicesData? userDevicesData)
    {
        if (userDevicesData is null)
            throw new InvalidOperationException("Refusing to persist incomplete user data.");

        if (userDevicesData.DeletedDevices.Count > MaxUserDataTombstonesPerList)
            throw new InvalidOperationException("Refusing to persist too many user data tombstones.");

        if (userDevicesData.Devices.Any(device =>
                device.Id == Guid.Empty ||
                device.LinkedAt == default ||
                !IsValidUserDeviceName(device.Name)))
            throw new InvalidOperationException("Refusing to persist invalid device data.");

        if (HasDuplicates(userDevicesData.Devices, device => device.Id))
            throw new InvalidOperationException("Refusing to persist duplicate device data.");

        if (HasDuplicates(userDevicesData.Devices, device => device.Name.Trim(), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to persist duplicate device names.");
    }


    private void EnsureCustomColorsCanBePersisted(UserPasswordsData passwordsData)
    {
        if (passwordsData.CustomColors.Any(color =>
                color.Id == Guid.Empty ||
                !IsValidARGBColor(color.ColorCode) ||
                !IsValidCustomUserColorName(color.ColorName)))
            throw new InvalidOperationException("Refusing to persist invalid custom color data.");

        if (HasDuplicates(passwordsData.CustomColors, color => color.Id))
            throw new InvalidOperationException("Refusing to persist duplicate custom color data.");

        if (HasDuplicates(passwordsData.CustomColors, color => color.ColorCode.Trim(), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to persist duplicate custom color codes.");

        var namedColors = passwordsData.CustomColors.Where(color => color.ColorName is not null);
        if (HasDuplicates(namedColors, color => color.ColorName!.Trim(), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to persist duplicate custom color names.");
    }


    private void EnsurePasswordTagsCanBePersisted(UserPasswordsData passwordsData)
    {
        if (passwordsData.Tags.Any(tag =>
                tag.Id == Guid.Empty ||
                !IsValidPasswordTagName(tag.Name) ||
                !IsValidARGBColor(tag.Color)))
            throw new InvalidOperationException("Refusing to persist invalid password tag data.");

        if (HasDuplicates(passwordsData.Tags, tag => tag.Id))
            throw new InvalidOperationException("Refusing to persist duplicate password tag data.");

        if (HasDuplicates(passwordsData.Tags, tag => tag.Name.Trim(), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to persist duplicate password tag names.");
    }


    private void EnsurePasswordTagReferencesCanBePersisted(UserPasswordsData passwordsData)
    {
        var existingTagIds = passwordsData.Tags.Select(tag => tag.Id).ToHashSet();
        var hasInvalidReferences = passwordsData.Passwords.Any(password =>
            password.TagIds.Any(tagId => tagId == Guid.Empty || !existingTagIds.Contains(tagId)) ||
            password.TagIds.Distinct().Count() != password.TagIds.Count);

        if (hasInvalidReferences)
            throw new InvalidOperationException("Refusing to persist invalid password tag references.");
    }


    private bool HasDuplicates<TItem, TKey>(
        IEnumerable<TItem> items,
        Func<TItem, TKey> keySelector,
        IEqualityComparer<TKey>? comparer = null) =>
        items.GroupBy(keySelector, comparer).Any(group => group.Count() != 1);


    private void EnsureBlobTimestamps(User user, DateTimeOffset value)
    {
        if (user.UserDataLastModifiedAt == default)
            user.UserDataLastModifiedAt = value;
        if (user.GeneralUserDataLastModifiedAt == default)
            user.GeneralUserDataLastModifiedAt = value;
        if (user.UserPasswordsDataLastModifiedAt == default)
            user.UserPasswordsDataLastModifiedAt = value;
        if (user.UserDevicesDataLastModifiedAt == default)
            user.UserDevicesDataLastModifiedAt = value;
    }


    public async Task<bool> UserExistsAsync(Guid uid, CancellationToken ct = default)
    {
        var user = await GetUserByUidAsync(uid, ct);
        return user is not null;
    }


    public Task DeleteUserAsync(User user, CancellationToken ct = default) =>
        DeleteUserAsync(user, false, ct);


    public async Task DeleteUserAsync(User user, bool enqueueSync, CancellationToken ct = default)
    {
        if (enqueueSync)
        {
            await _syncQueue.EnqueueAsync(new SyncItem
            {
                ModelId = user.UId,
                ModelType = SyncModelType.User,
                ChangeType = SyncChangeType.Deleted
            }, ct);
        }

        user.ClearEncryptedPayloads();
        _users.Delete(user);
        await _uow.SaveChangesAsync(ct);
        await _syncRuntime.RefreshSyncEnabledAsync(ct);
    }


    public Task DeleteUserAsync(Guid uid, CancellationToken ct = default) =>
        DeleteUserAsync(uid, false, ct);


    public async Task DeleteUserAsync(Guid uid, bool enqueueSync, CancellationToken ct = default)
    {
        var user = await GetAndVerifyUserByUidAsync(uid, ct);
        await DeleteUserAsync(user, enqueueSync, ct);
    }


    public Task DeleteUserByTokenAsync(Guid token, CancellationToken ct = default) =>
        DeleteUserByTokenAsync(token, false, ct);


    public async Task DeleteUserByTokenAsync(Guid token, bool enqueueSync, CancellationToken ct = default)
    {
        var user = await GetAndVerifyUserAsync(token, ct);
        await DeleteUserAsync(user, enqueueSync, ct);
    }
}
