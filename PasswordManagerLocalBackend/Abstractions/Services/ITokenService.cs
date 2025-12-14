namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface ITokenService
{
    Guid Issue(Guid uid);
    bool Validate(Guid token);
    bool TryGetUid(Guid token, out Guid uid);
    Guid GetUidOrThrow(Guid token);
    bool Revoke(Guid token);
    int PurgeExpired();
}