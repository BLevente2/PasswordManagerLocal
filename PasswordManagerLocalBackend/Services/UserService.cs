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




    public async Task<User> GetAndVerifyUserByUidAsync(Guid uid, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(uid, ct);
        if (user is null)
            throw new UserNotFoundException();

        user.VerifyIntegrity();
        return user;
    }


    public async Task<UserData> GetAndVerifyUserDataAsync(User user, EncryptionKey? key = null, Guid token = default)
    {
        if (key is null)
            key = GetEncryptionKey(token);

        var userData = await DecryptDecompressDeserializeAsync<UserData>(user.EncryptedPayload, key);
        if (userData is null)
            throw new UnauthorizedAccessException();

        userData.VerifyIntegrity();
        return userData;
    }


    public async Task<UserData> GetUserDataAsync(Guid token, Guid uid = default, CancellationToken ct = default)
    {
        if (uid == default)
            uid = GetUidFromToken(token);

        var userData = await _cache.GetOrLoadUserDataAsync(
            token,
            innerCt => LoadCachedUserDataAsync(uid, token, innerCt),
            ct);

        if (userData is null)
            throw new UserNotFoundException(uid);

        return userData;
    }


    public async Task<UserData?> LoadCachedUserDataAsync(Guid uid, Guid token, CancellationToken ct = default)
    {
        using var key = GetEncryptionKey(token);

        var user = await GetAndVerifyUserByUidAsync(uid, ct);
        return await GetAndVerifyUserDataAsync(user, key);
    }


    public async Task<User> GetUserByTokenAsync(Guid token, CancellationToken ct = default)
    {
        var uid = GetUidFromToken(token);
        return await GetAndVerifyUserByUidAsync(uid, ct);
    }

    public async Task<User> GetAndVerifyUserByUsernameAsync(byte[] username, CancellationToken ct = default)
    {
        var user = await GetUserByUsernameAsync(username, ct);
        if (user is null)
            throw new UserNotFoundException();
        user.VerifyIntegrity();
        return user;
    }


    public async Task<User?> GetUserByUsernameAsync(byte[] username, CancellationToken ct = default)
    {
        var users = await _users.ListAllAsync(ct);
        if (users.Count == 0)
            return null;

        foreach (var user in users)
        {
            var computedHash = Hashing.SHA256Hash(username, user.UsernameSalt);
            if (!Hashing.Verify(user.UsernameHash, computedHash))
                continue;

            user.VerifyIntegrity();
            return user;
        }

        return null;
    }


    public async Task UpdateAndSaveAsync(User user, CancellationToken ct = default)
    {
        user.GenerateIntegrityHash();
        _users.Update(user);
        await _uow.SaveChangesAsync(ct);
    }


    public async Task AddAndSaveAsync(User user, CancellationToken ct = default)
    {
        user.GenerateIntegrityHash();
        await _users.AddAsync(user, ct);
        await _uow.SaveChangesAsync(ct);
    }


    public async Task UpdateAndSaveAsync(UserData userData, User? user = null, EncryptionKey? key = null, Guid token = default, CancellationToken ct = default)
    {
        if (user is null)
            user = await GetAndVerifyUserByUidAsync(userData.UId, ct);

        if (key is null)
            key = GetEncryptionKey(token);

        userData.GenerateIntegrityHash();

        CryptographicOperations.ZeroMemory(user.EncryptedPayload);
        user.EncryptedPayload = await SerializeCompressEncryptAsync<UserData>(userData, key);

        await UpdateAndSaveAsync(user, ct);
    }



    public Task<IReadOnlyList<User>> GetRememberMeEnabledUsersAsync(CancellationToken ct = default) =>
        _users.GetAllRememberMeEnabledUsersAsync(ct);





    private EncryptionKey GetEncryptionKey(Guid token)
    {
        if (token == default)
            throw new UnauthorizedAccessException();

        if (!_keys.TryGetEncryptionKey(token, out var key))
            throw new InvalidTokenException();

        return key;
    }

    private Guid GetUidFromToken(Guid token)
    {
        if (!_tokens.TryGetUid(token, out var uid))
            throw new InvalidTokenException();
        return uid;
    }
}