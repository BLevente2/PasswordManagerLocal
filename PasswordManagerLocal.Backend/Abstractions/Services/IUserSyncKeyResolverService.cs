using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserSyncKeyResolverService
{
    bool TryResolve(User user, out EncryptionKey? key);

    bool TryResolve(User user, out EncryptionKey? key, out UserSyncKeyConfidence confidence)
    {
        var resolved = TryResolve(user, out key);
        confidence = resolved
            ? UserSyncKeyConfidence.ExplicitlyTrusted
            : UserSyncKeyConfidence.UnconfirmedPassword;
        return resolved;
    }
}
