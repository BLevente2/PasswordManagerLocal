using PasswordManagerLocalBackend.Abstractions.Security;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Security;
using System.Security.Cryptography;

namespace PasswordManagerLocalBackend.Services;

public class RememberMeService : IRememberMeService
{
    private readonly ITokenService _tokens;
    private readonly IKeyVaultService _keys;
    private readonly IKeyProtector _protector;
    private readonly IUserService _userService;

    public RememberMeService(
        ITokenService tokens,
        IKeyVaultService keys,
        IKeyProtector protector,
        IUserService userService)
    {
        _tokens = tokens;
        _keys = keys;
        _protector = protector;
        _userService = userService;
    }



    public async Task<IReadOnlyList<Guid>> InicializeAllRememberMeAsync(CancellationToken ct = default)
    {
        var initializedTokens = new List<Guid>();

        var usersEnabledRM = await _userService.GetAndVerifyRememberMeEnabledUsersAsync(ct);
        if (usersEnabledRM.Count == 0)
            return initializedTokens;

        foreach (var user in usersEnabledRM)
        {
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
        var user = await _userService.GetAndVerifyUserAsync(token, ct);
        using var key = _userService.GetEncryptionKeyFromToken(token);

        SetRememberMe(user, rememberMe, key);
        await _userService.UpdateUserAsync(user, ct);
    }


    public void SetRememberMe(User user, bool rememberMe, EncryptionKey key)
    {
        var isRememberMeCurrently = user.SavedKey is not null;

        if (!rememberMe)
        {
            if (!isRememberMeCurrently)
                return;

            CryptographicOperations.ZeroMemory(user.SavedKey);
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