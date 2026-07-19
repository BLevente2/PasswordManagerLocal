using PasswordManagerLocal.Backend.Security;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface ICredentialVerificationService
{
    bool IsPasswordValid(Guid token, byte[] password, byte[] salt);
    bool TryGetActiveUserEncryptionKey(Guid uid, out EncryptionKey? key);
}
