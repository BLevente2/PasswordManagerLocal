using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Security;
using static PasswordManagerLocalBackend.Utils.DataCodec;

namespace PasswordManagerLocalBackend.Services;

public sealed class UserService : IUserService
{
    private readonly IUserRepository _users;
    private readonly IDataCachingService _cache;
    private readonly IKeyVaultService _keys;

    public UserService(IUserRepository users, IDataCachingService cache, IKeyVaultService keys)
    {
        _users = users;
        _cache = cache;
        _keys = keys;
    }




    public async Task<UserData> GetUserDataAsync(Guid uid, string token, CancellationToken ct = default)
    {
        var userData = await _cache.GetOrLoadUserDataAsync(
            token,
            innerCt => LoadCachedUserDataAsync(uid, token, innerCt),
            ct
        );

        if (userData is null)
            throw new UserNotFoundException(uid);

        return userData;
    }


    public async Task<UserData?> LoadCachedUserDataAsync(Guid uid, string token, CancellationToken ct = default)
    {
        if (!_keys.TryGetEncryptionKey(token, out var key))
            return null;

        var user = await _users.GetByIdAsync(uid, ct);
        if (user is null)
            return null;

        if (!user.IsIntegrityValid())
            throw new InvalidDataIntegrityException(typeof(User));

        var userData = await DecryptDecompressDeserializeAsync<UserData>(user.EncryptedPayload, key);
        if (userData is null)
            return null;

        if (!userData.IsIntegrityValid())
            throw new InvalidDataIntegrityException(typeof(UserData));

        key.Dispose();
        return userData;
    }


    public async Task<User?> GetUserByTokenAsync(string token, CancellationToken ct = default)
    {
        if (!_cache.TryGetUserData(token, out var userData) || userData is null)
            return null;

        return await _users.GetByIdAsync(userData.UId, ct);
    }


    public async Task<User?> GetUserByUsernameAsync(byte[] username, CancellationToken ct = default)
    {
        var users = await _users.ListAllAsync();
        if (users.Count == 0)
            return null;

        foreach (var user in users)
        {
            var computedHash = Hashing.SHA256Hash(username, user.UsernameSalt);
            if (Hashing.Verify(user.UsernameHash, computedHash))
                return user;
        }
        return null;
    }
}