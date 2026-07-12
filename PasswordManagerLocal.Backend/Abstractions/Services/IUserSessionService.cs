using PasswordManagerLocal.Backend.Security;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserSessionService
{
    Guid GetUidFromToken(Guid token);
    EncryptionKey GetEncryptionKeyFromToken(Guid token);
}
