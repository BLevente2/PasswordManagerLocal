using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IDataCachingService
{
    Task<UserData?> GetOrLoadUserDataAsync(string token, Func<CancellationToken, Task<UserData?>> loader, CancellationToken ct = default);
    Task<UserData?> GetOrLoadUserDataAsync(string token, Func<Task<UserData?>> loader);
    bool TryGetUserData(string token, out UserData? value);
    void SetUserData(string token, UserData value);

    Task<GroupData?> GetOrLoadGroupDataAsync(string token, Guid groupId, Func<CancellationToken, Task<GroupData?>> loader, CancellationToken ct = default);
    Task<GroupData?> GetOrLoadGroupDataAsync(string token, Guid groupId, Func<Task<GroupData?>> loader);
    bool TryGetGroupData(string token, Guid groupId, out GroupData? value);
    void SetGroupData(string token, Guid groupId, GroupData value);

    void InvalidateGroup(string token, Guid groupId);
    void InvalidateToken(string token);
}