using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.State;

internal readonly struct InvalidationEntry
{
    public readonly AuthSessionInvalidationReason Reason;
    public readonly long ExpiresTicksUtc;

    public InvalidationEntry(AuthSessionInvalidationReason reason, long expiresTicksUtc)
    {
        Reason = reason;
        ExpiresTicksUtc = expiresTicksUtc;
    }
}
