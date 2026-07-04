using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Responses;

public sealed class AuthSessionStatusResponse
{
    public bool IsAuthenticated { get; set; }
    public AuthSessionInvalidationReason InvalidationReason { get; set; } = AuthSessionInvalidationReason.None;
    public DateTimeOffset? ExpiresAtUtc { get; set; }
}
