using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IAuthenticatedSessionIssuer
{
    Guid IssueAuthenticatedSession(Guid userId, EncryptionKey key, UserDataBundle bundle);
}
