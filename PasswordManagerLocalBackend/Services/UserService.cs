using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models;
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

        var userData = await DecryptDecompressDeserializeAsync<UserData>(user.EncryptedPayload, key);
        if (userData is null)
            return null;

        key.Dispose();
        return userData;
    }
}