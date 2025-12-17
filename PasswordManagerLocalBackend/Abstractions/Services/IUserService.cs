using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Security;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IUserService
{
    Guid GetUidFromToken(Guid token);
    EncryptionKey GetEncryptionKeyFromToken(Guid token);

    Task<User?> GetUserByUidAsync(Guid uid, CancellationToken ct = default);
    Task<User> GetAndVerifyUserByUidAsync(Guid uid, CancellationToken ct = default);
    Task<User> GetAndVerifyUserAsync(Guid token, CancellationToken ct = default);

    Task<User?> GetUserByUsernameAsync(byte[] username, CancellationToken ct = default);
    Task<User> GetAndVerifyUserByUsernameAsync(byte[] username, CancellationToken ct = default);

    Task<UserData> GetAndVerifyUserDataAsync(User user, EncryptionKey key);
    Task<UserData> GetAndVerifyUserDataAsync(User user, Guid token);
    Task<UserData> GetLoadAndVerifyUserDataAsync(Guid token, CancellationToken ct = default);

    Task<IReadOnlyList<User>> GetAndVerifyRememberMeEnabledUsersAsync(CancellationToken ct = default);

    Task AddNewUserAsync(User user, CancellationToken ct = default);

    Task UpdateUserAsync(User user, CancellationToken ct = default);

    Task UpdateUserDataAsync(UserData userData, User user, EncryptionKey key, CancellationToken ct = default);
    Task UpdateUserDataAsync(UserData userData, Guid token, EncryptionKey key, CancellationToken ct = default);
    Task UpdateUserDataAsync(UserData userData, Guid token, CancellationToken ct = default);

    Task<bool> UserExistsAsync(Guid uid, CancellationToken ct = default);

    Task DeleteUserAsync(User user, CancellationToken ct = default);
    Task DeleteUserAsync(Guid uid, CancellationToken ct = default);
    Task DeleteUserByTokenAsync(Guid token, CancellationToken ct = default);
}