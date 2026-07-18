using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeUserSyncKeyResolverService : IUserSyncKeyResolverService
{
    public bool TryResolve(User user, out EncryptionKey? key)
    {
        key = null;
        return false;
    }
}
