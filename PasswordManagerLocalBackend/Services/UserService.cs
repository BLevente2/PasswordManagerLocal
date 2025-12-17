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
    private readonly IUnitOfWork _uow;

    public UserService(IUserRepository users, IDataCachingService cache, IKeyVaultService keys, ITokenService tokens, IUnitOfWork uow)
    {
        _users = users;
        _cache = cache;
        _keys = keys;
        _tokens = tokens;
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


    public async Task<UserData> GetLoadAndVerifyUserDataAsync(Guid token, CancellationToken ct = default)
    {
        if (_cache.TryGetUserData(token, out var foundUserData) && foundUserData is not null)
        {
            foundUserData.VerifyIntegrity();
            return foundUserData;
        }

        var user = await GetAndVerifyUserAsync(token, ct);

        var userData = await GetAndVerifyUserDataAsync(user, token);
        _cache.SetUserData(token, userData);
        return userData;
    }


    public async Task<IReadOnlyList<User>> GetAndVerifyRememberMeEnabledUsersAsync(CancellationToken ct = default)
    {
        var users = await _users.GetAllRememberMeEnabledUsersAsync(ct);
        foreach (var user in users)
        {
            user.VerifyIntegrity();
        }
        return users;
    }


    public async Task AddNewUserAsync(User user, CancellationToken ct = default)
    {
        user.GenerateIntegrityHash();
        await _users.AddAsync(user, ct);
        await _uow.SaveChangesAsync(ct);
    }


    public async Task UpdateUserAsync(User user, CancellationToken ct = default)
    {
        user.GenerateIntegrityHash();
        _users.Update(user);
        await _uow.SaveChangesAsync(ct);
    }


    public async Task UpdateUserDataAsync(UserData userData, User user, EncryptionKey key, CancellationToken ct = default)
    {
        userData.GenerateIntegrityHash();
        var newEncryptedPayload = await SerializeCompressEncryptAsync<UserData>(userData, key);
        CryptographicOperations.ZeroMemory(user.EncryptedPayload);
        user.EncryptedPayload = newEncryptedPayload;
        await UpdateUserAsync(user, ct);
    }


    public async Task UpdateUserDataAsync(UserData userData, Guid token, EncryptionKey key, CancellationToken ct = default)
    {
        var user = await GetAndVerifyUserAsync(token, ct);
        await UpdateUserDataAsync(userData, user, key, ct);
    }


    public async Task UpdateUserDataAsync(UserData userData, Guid token, CancellationToken ct = default)
    {
        using var key = GetEncryptionKeyFromToken(token);
        await UpdateUserDataAsync(userData, token, key, ct);
    }

    public async Task<bool> UserExistsAsync(Guid uid, CancellationToken ct = default)
    {
        var user = await GetUserByUidAsync(uid, ct);
        return user is not null;
    }


    public async Task DeleteUserAsync(User user, CancellationToken ct = default)
    {
        _users.Delete(user);
        await _uow.SaveChangesAsync(ct);
    }


    public async Task DeleteUserAsync(Guid uid, CancellationToken ct = default)
    {
        var user = await GetAndVerifyUserByUidAsync(uid, ct);
        await DeleteUserAsync(user, ct);
    }


    public async Task DeleteUserByTokenAsync(Guid token, CancellationToken ct = default)
    {
        var user = await GetAndVerifyUserAsync(token, ct);
        await DeleteUserAsync(user, ct);
    }
}