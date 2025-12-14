using PasswordManagerLocalBackend.Abstractions.Persistence;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Security;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Security;
using System.Security.Cryptography;

namespace PasswordManagerLocalBackend.Services;

public class RememberMeService : IRememberMeService
{
    private readonly IUnitOfWork _uow;
    private readonly ITokenService _tokens;
    private readonly IKeyVaultService _keys;
    private readonly IUserRepository _users;
    private readonly IKeyProtector _protector;
    private readonly IUserService _userService;

    public RememberMeService(
        IUnitOfWork uow,
        ITokenService tokens,
        IKeyVaultService keys,
        IUserRepository users,
        IKeyProtector protector,
        IUserService userService)
    {
        _uow = uow;
        _tokens = tokens;
        _keys = keys;
        _users = users;
        _protector = protector;
        _userService = userService;
    }



    public async Task<IReadOnlyList<Guid>> InicializeAllRememberMeAsync(CancellationToken ct = default)
    {
        var initializedTokens = new List<Guid>();

        var usersEnabledRM = await _users.GetAllRememberMeEnabledUsersAsync(ct);
        if (usersEnabledRM.Count == 0)
            return initializedTokens;

        foreach (var user in usersEnabledRM)
        {
            user.VerifyIntegrity();

            var rawKey = _protector.Unprotect(user.SavedKey);
            try
            {
                using var key = EncryptionKey.FromRaw(rawKey);
                var token = _tokens.Issue(user.UId);
                _keys.SetUserKey(token, key);
                initializedTokens.Add(token);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(rawKey);
            }
        }

        return initializedTokens;
    }


    public async Task SetRememberMeAsync(Guid token, bool rememberMe, CancellationToken ct = default)
    {
        var user = await _userService.GetUserByTokenAsync(token, ct);
        if (user is null)
            throw new UserNotFoundException();

        if (rememberMe)
        {
            if (!_keys.TryGetEncryptionKey(token, out var key))
                throw new InvalidTokenException();

            try
            {
                var raw = key.ExportCopy();
                try
                {
                    user.SavedKey = _protector.Protect(raw);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(raw);
                }
            }
            finally
            {
                key.Dispose();
            }
        }
        else
        {
            if (user.SavedKey is null)
                return;

            user.SavedKey = null;
        }

        user.GenerateIntegrityHash();
        _users.Update(user);
        await _uow.SaveChangesAsync(ct);
    }


    public void SetRememberMe(User user, bool rememberMe, EncryptionKey key)
    {
        if (!rememberMe)
        {
            user.SavedKey = null;
            return;
        }

        var raw = key.ExportCopy();
        try
        {
            user.SavedKey = _protector.Protect(raw);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }
    }
}