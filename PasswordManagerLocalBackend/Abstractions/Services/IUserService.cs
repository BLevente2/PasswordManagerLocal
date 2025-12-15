using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Security;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IUserService
{
    Task<User> GetAndVerifyUserByUidAsync(Guid uid, CancellationToken ct = default);
    Task<UserData> GetAndVerifyUserDataAsync(User user, EncryptionKey? key = null, Guid token = default);
    Task<UserData> GetUserDataAsync(Guid token, Guid uid = default, CancellationToken ct = default);
    Task<UserData?> LoadCachedUserDataAsync(Guid uid, Guid token, CancellationToken ct = default);
    Task<User> GetUserByTokenAsync(Guid token, CancellationToken ct = default);
    Task<User> GetAndVerifyUserByUsernameAsync(byte[] username, CancellationToken ct = default);
    Task<User?> GetUserByUsernameAsync(byte[] username, CancellationToken ct = default);
    Task UpdateAndSaveAsync(User user, CancellationToken ct = default);
    Task AddAndSaveAsync(User user, CancellationToken ct = default);
    Task UpdateAndSaveAsync(UserData userData, User? user = null, EncryptionKey? key = null, Guid token = default, CancellationToken ct = default);
    Task<IReadOnlyList<User>> GetRememberMeEnabledUsersAsync(CancellationToken ct = default);
}