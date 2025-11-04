using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Security;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IUserService
{
    Task<UserData> GetUserDataAsync(User user, string token);
    Task<UserData> GetUserDataSync(User user, string token, EncryptionKey key);
}