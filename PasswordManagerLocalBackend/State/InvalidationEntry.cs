using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.State;

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
