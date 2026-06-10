using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface ITokenService
{
    Guid Issue(Guid uid);
    bool Validate(Guid token);
    bool TryGetUid(Guid token, out Guid uid);
    Guid GetUidOrThrow(Guid token);
    IReadOnlyList<Guid> ListTokensByUid(Guid uid);
    bool Revoke(Guid token);
    bool Revoke(Guid token, AuthSessionInvalidationReason reason);
    bool TryGetInvalidationReason(Guid token, out AuthSessionInvalidationReason reason);
    int PurgeExpired();
}