using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserSyncKeyResolverService
{
    bool TryResolve(User user, out EncryptionKey? key);
}
