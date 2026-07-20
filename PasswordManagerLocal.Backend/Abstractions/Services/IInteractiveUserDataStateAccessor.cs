using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IInteractiveUserDataStateAccessor
{
    Guid GetUserIdFromToken(Guid token);
    EncryptionKey GetEncryptionKeyFromToken(Guid token);
    void SetUserBlobKeys(Guid token, UserData userData);
    bool TryGetUserData(Guid token, out UserData? value);
    bool TryGetUserDataBundle(Guid token, out UserDataBundle? value);
    void SetUserDataBundle(Guid token, UserDataBundle value);
    void InvalidateToken(Guid token);
}
