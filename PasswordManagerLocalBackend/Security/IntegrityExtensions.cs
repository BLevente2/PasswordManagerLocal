using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Security;

public static class IntegrityExtensions
{
    public static void EnsureIntegrity(this User user)
    {
        if (user is null)
            throw new ArgumentNullException(nameof(user));

        if (!user.IsIntegrityValid())
            throw new InvalidDataIntegrityException(typeof(User));
    }

    public static void EnsureIntegrity(this UserData userData)
    {
        if (userData is null) throw new ArgumentNullException(nameof(userData));
        if (!userData.IsIntegrityValid())
            throw new InvalidDataIntegrityException(typeof(UserData));
    }
}