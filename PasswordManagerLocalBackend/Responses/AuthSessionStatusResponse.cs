using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Responses;

public sealed class AuthSessionStatusResponse
{
    public bool IsAuthenticated { get; set; }
    public AuthSessionInvalidationReason InvalidationReason { get; set; } = AuthSessionInvalidationReason.None;
}
