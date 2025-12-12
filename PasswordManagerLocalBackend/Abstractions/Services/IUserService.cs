using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Models.Encrypted;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IUserService
{
    Task<UserData> GetUserDataAsync(Guid uid, string token, CancellationToken ct = default);
    Task<UserData?> LoadCachedUserDataAsync(Guid uid, string token, CancellationToken ct = default);
    Task<User?> GetUserByTokenAsync(string token, CancellationToken ct = default);
    Task<User?> GetUserByUsernameAsync(byte[] username, CancellationToken ct = default);
}