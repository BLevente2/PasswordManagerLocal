using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Models.Encrypted;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IUserService
{
    Task<UserData> GetUserDataAsync(Guid token, Guid uid = default, CancellationToken ct = default);
    Task<UserData?> LoadCachedUserDataAsync(Guid uid, Guid token, CancellationToken ct = default);
    Task<User?> GetUserByTokenAsync(Guid token, CancellationToken ct = default);
    Task<User?> GetUserByUsernameAsync(byte[] username, CancellationToken ct = default);
}