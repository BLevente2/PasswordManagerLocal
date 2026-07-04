using PasswordManagerLocal.Backend.Abstractions.Security;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;
using System.Security.Cryptography;

namespace PasswordManagerLocal.Backend.Services;

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
            var token = await TryInitializeRememberedUserAsync(user, ct);
            if (token is not null)
                initializedTokens.Add(token.Value);
        }

        return initializedTokens;
    }


    public async Task<Guid> InitializeRememberMeSessionAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _userService.GetAndVerifyUserByUidAsync(userId, ct);
        var token = await TryInitializeRememberedUserAsync(user, ct);

        if (token is null)
            throw new InvalidTokenException();

        return token.Value;
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


    private async Task<Guid?> TryInitializeRememberedUserAsync(User user, CancellationToken ct)
    {
        if (user.SavedKey is null)
            return null;

        byte[]? rawKey = null;

        try
        {
            rawKey = _protector.Unprotect(user.SavedKey);
            using var key = EncryptionKey.FromRaw(rawKey);
            var token = _tokens.Issue(user.UId);
            _keys.SetUserKey(token, key);
            return token;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            await DisableBrokenRememberMeAsync(user, ct);
            return null;
        }
        finally
        {
            if (rawKey is not null)
                CryptographicOperations.ZeroMemory(rawKey);
        }
    }


    private async Task DisableBrokenRememberMeAsync(User user, CancellationToken ct)
    {
        try
        {
            if (user.SavedKey is not null)
                CryptographicOperations.ZeroMemory(user.SavedKey);

            user.SavedKey = null;
            await _userService.UpdateUserAsync(user, ct);
        }
        catch
        {
        }
    }
}
