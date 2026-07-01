using PasswordManagerLocalBackend.Abstractions.Persistence;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Security;
using PasswordManagerLocalBackend.Sync;
using PasswordManagerLocalBackend.Utils;
using System.Security.Cryptography;
using static PasswordManagerLocalBackend.Utils.DataCodec;
using static PasswordManagerLocalBackend.Utils.DataValidationUtil;
using static PasswordManagerLocalBackend.Constants.PasswordConstants;
using static PasswordManagerLocalBackend.Constants.TombstoneConstants;

namespace PasswordManagerLocalBackend.Services;

public sealed class UserService : IUserService
{
    private readonly IUserRepository _users;
    private readonly IDataCachingService _cache;
    private readonly IKeyVaultService _keys;
    private readonly ITokenService _tokens;
    private readonly ISyncQueueService _syncQueue;
    private readonly ISyncRuntimeService _syncRuntime;
    private readonly IUnitOfWork _uow;

    public UserService(
        IUserRepository users,
        IDataCachingService cache,
        IKeyVaultService keys,
        ITokenService tokens,
        ISyncQueueService syncQueue,
        ISyncRuntimeService syncRuntime,
        IUnitOfWork uow)
    {
        _users = users;
        _cache = cache;
        _keys = keys;
        _tokens = tokens;
        _syncQueue = syncQueue;
        _syncRuntime = syncRuntime;
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
        var users = await _users.ListAllAsync(ct);

        foreach (var user in users)
        {
            var calcualtedHash = Hashing.SHA256Hash(username, user.UsernameSalt);
            try
            {
                if (Hashing.Verify(user.UsernameHash, calcualtedHash))
                    return user;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(calcualtedHash);
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
        VerifyUserDataIntegrity(userData);
        return userData;
    }


    public async Task<UserData> GetAndVerifyUserDataAsync(User user, Guid token)
    {
        using var key = GetEncryptionKeyFromToken(token);
        return await GetAndVerifyUserDataAsync(user, key);
    }


    public async Task<UserDataBundle> GetAndVerifyUserDataBundleAsync(User user, EncryptionKey key, CancellationToken ct = default)
    {
        var userData = await DecryptUserDataAsync(user, key, ct);
        VerifyUserDataIntegrity(userData);

        var generalUserData = await DecryptBlobAsync(
            user.EncryptedGeneralUserDataPayload,
            userData.GeneralUserDataKey,
            BackendJsonSerializerContext.Default.GeneralUserData,
            ct);

        var userPasswordsData = await DecryptBlobAsync(
            user.EncryptedUserPasswordsDataPayload,
            userData.UserPasswordsDataKey,
            BackendJsonSerializerContext.Default.UserPasswordsData,
            ct);

        var userDevicesData = await DecryptBlobAsync(
            user.EncryptedUserDevicesDataPayload,
            userData.UserDevicesDataKey,
            BackendJsonSerializerContext.Default.UserDevicesData,
            ct);

        var bundle = new UserDataBundle
        {
            UserData = userData,
            GeneralUserData = generalUserData,
            UserPasswordsData = userPasswordsData,
            UserDevicesData = userDevicesData
        };

        VerifyUserDataBundleIntegrity(bundle);
        return bundle;
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
            VerifyUserDataIntegrity(foundUserData);
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
            VerifyUserDataBundleIntegrity(foundBundle);
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
        ReplaceUserBlobKeys(bundle.UserData);
        await PersistUserDataBundleAsync(bundle, user, newUserKey, UserDataBlobKind.All, true, enqueueSync, ct);
    }


    private async Task PersistUserDataBundleAsync(UserDataBundle bundle, User user, EncryptionKey userKey, UserDataBlobKind modifiedBlobs, bool forceRewriteAllBlobs, bool enqueueSync, CancellationToken ct)
    {
        GenerateAndCopyChildIntegrityHashes(bundle);
        bundle.UserData.GenerateIntegrityHash();
        EnsureUserDataBundleCanBePersisted(bundle, user);
        VerifyUserDataBundleIntegrity(bundle);

        var now = DateTimeOffset.UtcNow;
        if (modifiedBlobs != UserDataBlobKind.None || forceRewriteAllBlobs)
            user.UserDataLastModifiedAt = now;

        if (forceRewriteAllBlobs || modifiedBlobs.HasFlag(UserDataBlobKind.General))
        {
            user.GeneralUserDataLastModifiedAt = now;
            await ReplaceEncryptedGeneralUserDataPayloadAsync(user, bundle, ct);
        }

        if (forceRewriteAllBlobs || modifiedBlobs.HasFlag(UserDataBlobKind.Passwords))
        {
            user.UserPasswordsDataLastModifiedAt = now;
            await ReplaceEncryptedUserPasswordsDataPayloadAsync(user, bundle, ct);
        }

        if (forceRewriteAllBlobs || modifiedBlobs.HasFlag(UserDataBlobKind.Devices))
        {
            user.UserDevicesDataLastModifiedAt = now;
            await ReplaceEncryptedUserDevicesDataPayloadAsync(user, bundle, ct);
        }

        var encryptedUserData = await SerializeCompressEncryptAsync(bundle.UserData, userKey, BackendJsonSerializerContext.Default.UserData, ct: ct);
        CryptographicOperations.ZeroMemory(user.EncryptedPayload);
        user.EncryptedPayload = encryptedUserData;

        await UpdateUserAsync(user, enqueueSync, ct);
    }


    private async Task ReplaceEncryptedGeneralUserDataPayloadAsync(User user, UserDataBundle bundle, CancellationToken ct)
    {
        using var key = EncryptionKey.FromRaw(bundle.UserData.GeneralUserDataKey);
        var encrypted = await SerializeCompressEncryptAsync(bundle.GeneralUserData, key, BackendJsonSerializerContext.Default.GeneralUserData, ct: ct);
        CryptographicOperations.ZeroMemory(user.EncryptedGeneralUserDataPayload);
        user.EncryptedGeneralUserDataPayload = encrypted;
    }


    private async Task ReplaceEncryptedUserPasswordsDataPayloadAsync(User user, UserDataBundle bundle, CancellationToken ct)
    {
        using var key = EncryptionKey.FromRaw(bundle.UserData.UserPasswordsDataKey);
        var encrypted = await SerializeCompressEncryptAsync(bundle.UserPasswordsData, key, BackendJsonSerializerContext.Default.UserPasswordsData, ct: ct);
        CryptographicOperations.ZeroMemory(user.EncryptedUserPasswordsDataPayload);
        user.EncryptedUserPasswordsDataPayload = encrypted;
    }


    private async Task ReplaceEncryptedUserDevicesDataPayloadAsync(User user, UserDataBundle bundle, CancellationToken ct)
    {
        using var key = EncryptionKey.FromRaw(bundle.UserData.UserDevicesDataKey);
        var encrypted = await SerializeCompressEncryptAsync(bundle.UserDevicesData, key, BackendJsonSerializerContext.Default.UserDevicesData, ct: ct);
        CryptographicOperations.ZeroMemory(user.EncryptedUserDevicesDataPayload);
        user.EncryptedUserDevicesDataPayload = encrypted;
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


    public static byte[] GenerateBlobKey()
    {
        using var key = EncryptionKey.Create();
        return key.ExportCopy();
    }


    public static void InitializeUserDataKeys(UserData userData)
    {
        ReplaceGeneralUserDataKey(userData);
        ReplaceUserPasswordsDataKey(userData);
        ReplaceUserDevicesDataKey(userData);
    }


    private static void ReplaceUserBlobKeys(UserData userData)
    {
        ReplaceGeneralUserDataKey(userData);
        ReplaceUserPasswordsDataKey(userData);
        ReplaceUserDevicesDataKey(userData);
    }


    private static void ReplaceGeneralUserDataKey(UserData userData)
    {
        CryptographicOperations.ZeroMemory(userData.GeneralUserDataKey);
        userData.GeneralUserDataKey = GenerateBlobKey();
    }


    private static void ReplaceUserPasswordsDataKey(UserData userData)
    {
        CryptographicOperations.ZeroMemory(userData.UserPasswordsDataKey);
        userData.UserPasswordsDataKey = GenerateBlobKey();
    }


    private static void ReplaceUserDevicesDataKey(UserData userData)
    {
        CryptographicOperations.ZeroMemory(userData.UserDevicesDataKey);
        userData.UserDevicesDataKey = GenerateBlobKey();
    }


    private static void GenerateAndCopyChildIntegrityHashes(UserDataBundle bundle)
    {
        foreach (var password in bundle.UserPasswordsData.Passwords)
            password.GenerateIntegrityHash();
        foreach (var deleted in bundle.UserPasswordsData.DeletedPasswords)
            deleted.GenerateIntegrityHash();
        foreach (var color in bundle.UserPasswordsData.CustomColors)
            color.GenerateIntegrityHash();
        foreach (var deleted in bundle.UserPasswordsData.DeletedCustomColors)
            deleted.GenerateIntegrityHash();
        foreach (var tag in bundle.UserPasswordsData.Tags)
            tag.GenerateIntegrityHash();
        foreach (var deleted in bundle.UserPasswordsData.DeletedTags)
            deleted.GenerateIntegrityHash();
        bundle.UserPasswordsData.GenerateIntegrityHash();

        foreach (var device in bundle.UserDevicesData.Devices)
            device.GenerateIntegrityHash();
        foreach (var deleted in bundle.UserDevicesData.DeletedDevices)
            deleted.GenerateIntegrityHash();
        bundle.UserDevicesData.GenerateIntegrityHash();

        bundle.GeneralUserData.GenerateIntegrityHash();

        CryptographicOperations.ZeroMemory(bundle.UserData.GeneralUserDataIntegrityHash);
        CryptographicOperations.ZeroMemory(bundle.UserData.UserPasswordsDataIntegrityHash);
        CryptographicOperations.ZeroMemory(bundle.UserData.UserDevicesDataIntegrityHash);
        bundle.UserData.GeneralUserDataIntegrityHash = bundle.GeneralUserData.IntegrityHash.ToArray();
        bundle.UserData.UserPasswordsDataIntegrityHash = bundle.UserPasswordsData.IntegrityHash.ToArray();
        bundle.UserData.UserDevicesDataIntegrityHash = bundle.UserDevicesData.IntegrityHash.ToArray();
    }


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

        if (bundle.UserPasswordsData.PasswordKey.Length == 0 || bundle.UserDevicesData is null)
            throw new InvalidOperationException("Refusing to persist incomplete user data.");

        if (bundle.UserPasswordsData.Passwords.Count > MaxNumberOfPasswords)
            throw new InvalidOperationException("Refusing to persist too many passwords.");

        if (bundle.UserPasswordsData.CustomColors.Count > MaxNumberOfCustomUserColors)
            throw new InvalidOperationException("Refusing to persist too many custom colors.");

        if (bundle.UserPasswordsData.Tags.Count > MaxNumberOfPasswordTags)
            throw new InvalidOperationException("Refusing to persist too many password tags.");

        if (bundle.UserPasswordsData.DeletedPasswords.Count > MaxUserDataTombstonesPerList ||
            bundle.UserPasswordsData.DeletedCustomColors.Count > MaxUserDataTombstonesPerList ||
            bundle.UserPasswordsData.DeletedTags.Count > MaxUserDataTombstonesPerList ||
            bundle.UserDevicesData.DeletedDevices.Count > MaxUserDataTombstonesPerList)
            throw new InvalidOperationException("Refusing to persist too many user data tombstones.");

        if (bundle.UserDevicesData.Devices.Any(device =>
                device.Id == Guid.Empty ||
                device.LinkedAt == default ||
                !IsValidUserDeviceName(device.Name)))
            throw new InvalidOperationException("Refusing to persist invalid device data.");

        if (bundle.UserDevicesData.Devices
            .GroupBy(device => device.Id)
            .Any(group => group.Count() != 1))
            throw new InvalidOperationException("Refusing to persist duplicate device data.");

        if (bundle.UserDevicesData.Devices
            .GroupBy(device => device.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() != 1))
            throw new InvalidOperationException("Refusing to persist duplicate device names.");

        if (bundle.UserPasswordsData.CustomColors.Any(color =>
                color.Id == Guid.Empty ||
                !IsValidARGBColor(color.ColorCode) ||
                !IsValidCustomUserColorName(color.ColorName)))
            throw new InvalidOperationException("Refusing to persist invalid custom color data.");

        if (bundle.UserPasswordsData.CustomColors
            .GroupBy(color => color.Id)
            .Any(group => group.Count() != 1))
            throw new InvalidOperationException("Refusing to persist duplicate custom color data.");

        if (bundle.UserPasswordsData.CustomColors
            .GroupBy(color => color.ColorCode.Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() != 1))
            throw new InvalidOperationException("Refusing to persist duplicate custom color codes.");

        if (bundle.UserPasswordsData.CustomColors
            .Where(color => color.ColorName is not null)
            .GroupBy(color => color.ColorName!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() != 1))
            throw new InvalidOperationException("Refusing to persist duplicate custom color names.");

        if (bundle.UserPasswordsData.Tags.Any(tag =>
                tag.Id == Guid.Empty ||
                !IsValidPasswordTagName(tag.Name) ||
                !IsValidARGBColor(tag.Color)))
            throw new InvalidOperationException("Refusing to persist invalid password tag data.");

        if (bundle.UserPasswordsData.Tags
            .GroupBy(tag => tag.Id)
            .Any(group => group.Count() != 1))
            throw new InvalidOperationException("Refusing to persist duplicate password tag data.");

        if (bundle.UserPasswordsData.Tags
            .GroupBy(tag => tag.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() != 1))
            throw new InvalidOperationException("Refusing to persist duplicate password tag names.");

        var existingTagIds = bundle.UserPasswordsData.Tags.Select(tag => tag.Id).ToHashSet();
        if (bundle.UserPasswordsData.Passwords.Any(password =>
                password.TagIds.Any(tagId => tagId == Guid.Empty || !existingTagIds.Contains(tagId)) ||
                password.TagIds.Distinct().Count() != password.TagIds.Count))
            throw new InvalidOperationException("Refusing to persist invalid password tag references.");
    }

    private void VerifyUserDataIntegrity(UserData userData) =>
        userData.VerifyIntegrity();


    private void VerifyUserDataBundleIntegrity(UserDataBundle bundle)
    {
        bundle.UserData.VerifyIntegrity();

        bundle.GeneralUserData.VerifyIntegrity();
        VerifyStoredChildHash(bundle.UserData.GeneralUserDataIntegrityHash, bundle.GeneralUserData.IntegrityHash, typeof(GeneralUserData));

        bundle.UserPasswordsData.VerifyIntegrity();
        foreach (var password in bundle.UserPasswordsData.Passwords)
            password.VerifyIntegrity();
        foreach (var deleted in bundle.UserPasswordsData.DeletedPasswords)
            deleted.VerifyIntegrity();
        foreach (var color in bundle.UserPasswordsData.CustomColors)
            color.VerifyIntegrity();
        foreach (var deleted in bundle.UserPasswordsData.DeletedCustomColors)
            deleted.VerifyIntegrity();
        foreach (var tag in bundle.UserPasswordsData.Tags)
            tag.VerifyIntegrity();
        foreach (var deleted in bundle.UserPasswordsData.DeletedTags)
            deleted.VerifyIntegrity();
        VerifyStoredChildHash(bundle.UserData.UserPasswordsDataIntegrityHash, bundle.UserPasswordsData.IntegrityHash, typeof(UserPasswordsData));

        bundle.UserDevicesData.VerifyIntegrity();
        foreach (var device in bundle.UserDevicesData.Devices)
            device.VerifyIntegrity();
        foreach (var deleted in bundle.UserDevicesData.DeletedDevices)
            deleted.VerifyIntegrity();
        VerifyStoredChildHash(bundle.UserData.UserDevicesDataIntegrityHash, bundle.UserDevicesData.IntegrityHash, typeof(UserDevicesData));
    }


    private static void VerifyStoredChildHash(byte[] expected, byte[] actual, Type type)
    {
        if (expected.Length != Hashing.SHA256HashSizeInBytes ||
            actual.Length != Hashing.SHA256HashSizeInBytes ||
            !Hashing.Verify(expected, actual))
            throw new InvalidDataIntegrityException(type);
    }


    private static void EnsureBlobTimestamps(User user, DateTimeOffset value)
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
