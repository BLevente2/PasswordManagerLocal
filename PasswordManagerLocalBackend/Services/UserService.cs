using PasswordManagerLocalBackend.Abstractions.Persistence;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Security;
using System.Security.Cryptography;
using static PasswordManagerLocalBackend.Utils.DataCodec;

namespace PasswordManagerLocalBackend.Services;

public sealed class UserService : IUserService
{
    private readonly IUserRepository _users;
    private readonly IDataCachingService _cache;
    private readonly IKeyVaultService _keys;
    private readonly ITokenService _tokens;
    private readonly ISyncQueueService _syncQueue;
    private readonly IUnitOfWork _uow;

    public UserService(
        IUserRepository users,
        IDataCachingService cache,
        IKeyVaultService keys,
        ITokenService tokens,
        ISyncQueueService syncQueue,
        IUnitOfWork uow)
    {
        _users = users;
        _cache = cache;
        _keys = keys;
        _tokens = tokens;
        _syncQueue = syncQueue;
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
        var userData = await DecryptDecompressDeserializeAsync<UserData>(user.EncryptedPayload, key);
        if (userData is null)
            throw new UnauthorizedAccessException();

        userData.VerifyIntegrity();
        return userData;
    }


    public async Task<UserData> GetAndVerifyUserDataAsync(User user, Guid token)
    {
        using var key = GetEncryptionKeyFromToken(token);
        return await GetAndVerifyUserDataAsync(user, key);
    }


    public bool TryGetAndVerifyUserDataFromCache(Guid token, out UserData? userData)
    {
        if (_cache.TryGetUserData(token, out var foundUserData) && foundUserData is not null)
        {
            foundUserData.VerifyIntegrity();
            userData = foundUserData;
            return true;
        }

        userData = null;
        return false;
    }


    public async Task<UserData> GetLoadAndVerifyUserDataAsync(Guid token, CancellationToken ct = default, User? user = null)
    {
        if (TryGetAndVerifyUserDataFromCache(token, out var foundUserData) && foundUserData is not null)
            return foundUserData;

        if (user is null)
            user = await GetAndVerifyUserAsync(token, ct);

        var userData = await GetAndVerifyUserDataAsync(user, token);
        _cache.SetUserData(token, userData);
        return userData;
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
        user.LastModifiedAt = DateTimeOffset.UtcNow;
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
        var newEncryptedPayload = await SerializeCompressEncryptAsync<UserData>(userData, key);
        CryptographicOperations.ZeroMemory(user.EncryptedPayload);
        user.EncryptedPayload = newEncryptedPayload;
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

    private static void EnsureUserDataCanBePersisted(UserData userData, User user)
    {
        if (userData.UId == Guid.Empty || userData.UId != user.UId)
            throw new InvalidOperationException("Refusing to persist invalid user data.");

        if (userData.Passwords.PasswordKey.Length == 0)
            throw new InvalidOperationException("Refusing to persist incomplete user data.");
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

        _users.Delete(user);
        await _uow.SaveChangesAsync(ct);
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