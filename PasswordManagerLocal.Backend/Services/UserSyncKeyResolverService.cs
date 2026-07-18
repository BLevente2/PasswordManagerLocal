using PasswordManagerLocal.Backend.Abstractions.Security;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;
using System.Security.Cryptography;

namespace PasswordManagerLocal.Backend.Services;

public sealed class UserSyncKeyResolverService : IUserSyncKeyResolverService
{
    private readonly IAuthService _auth;
    private readonly IKeyProtector _protector;

    public UserSyncKeyResolverService(IAuthService auth, IKeyProtector protector)
    {
        _auth = auth;
        _protector = protector;
    }

    public bool TryResolve(User user, out EncryptionKey? key)
    {
        if (_auth.TryGetActiveUserEncryptionKey(user.UId, out key) && key is not null)
            return true;

        key = null;
        if (user.SavedKey is null || user.SavedKey.Length == 0)
            return false;

        byte[]? raw = null;
        try
        {
            raw = _protector.Unprotect(user.SavedKey);
            key = EncryptionKey.FromRaw(raw);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            key = null;
            return false;
        }
        finally
        {
            if (raw is not null)
                CryptographicOperations.ZeroMemory(raw);
        }
    }
}
