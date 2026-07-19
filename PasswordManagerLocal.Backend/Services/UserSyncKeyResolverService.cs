using PasswordManagerLocal.Backend.Abstractions.Security;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;
using System.Security.Cryptography;

namespace PasswordManagerLocal.Backend.Services;

public sealed class UserSyncKeyResolverService : IUserSyncKeyResolverService
{
    private readonly ITokenService _tokens;
    private readonly IKeyVaultService _keys;
    private readonly IKeyProtector _protector;

    public UserSyncKeyResolverService(ITokenService tokens, IKeyVaultService keys, IKeyProtector protector)
    {
        _tokens = tokens;
        _keys = keys;
        _protector = protector;
    }

    public bool TryResolve(User user, out EncryptionKey? key) =>
        TryResolve(user, out key, out _);

    public bool TryResolve(User user, out EncryptionKey? key, out UserSyncKeyConfidence confidence)
    {
        foreach (var token in _tokens.ListTokensByUid(user.UId))
        {
            if (_keys.TryGetEncryptionKey(token, out key) && key is not null)
            {
                confidence = UserSyncKeyConfidence.AuthenticatedSession;
                return true;
            }
        }

        key = null;
        confidence = UserSyncKeyConfidence.UnconfirmedPassword;
        if (user.SavedKey is null || user.SavedKey.Length == 0)
            return false;

        byte[]? raw = null;
        try
        {
            raw = _protector.Unprotect(user.SavedKey);
            key = EncryptionKey.FromRaw(raw);
            confidence = UserSyncKeyConfidence.RememberMe;
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            key = null;
            confidence = UserSyncKeyConfidence.UnconfirmedPassword;
            return false;
        }
        finally
        {
            if (raw is not null)
                CryptographicOperations.ZeroMemory(raw);
        }
    }
}
