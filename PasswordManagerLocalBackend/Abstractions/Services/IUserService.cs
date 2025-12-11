using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IUserService
{
    Task<UserData> GetUserDataAsync(Guid uid, string token, CancellationToken ct = default);
    Task<UserData?> LoadCachedUserDataAsync(Guid uid, string token, CancellationToken ct = default);
}