using PasswordManagerLocalBackend.Models.Encrypted;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IDataCachingService
{
    Task<UserData?> GetOrLoadUserDataAsync(Guid token, Func<CancellationToken, Task<UserData?>> loader, CancellationToken ct = default);
    Task<UserData?> GetOrLoadUserDataAsync(Guid token, Func<Task<UserData?>> loader);
    bool TryGetUserData(Guid token, out UserData? value);
    void SetUserData(Guid token, UserData value);

    Task<GroupData?> GetOrLoadGroupDataAsync(Guid token, Guid groupId, Func<CancellationToken, Task<GroupData?>> loader, CancellationToken ct = default);
    Task<GroupData?> GetOrLoadGroupDataAsync(Guid token, Guid groupId, Func<Task<GroupData?>> loader);
    bool TryGetGroupData(Guid token, Guid groupId, out GroupData? value);
    void SetGroupData(Guid token, Guid groupId, GroupData value);

    void InvalidateGroup(Guid token, Guid groupId);
    void InvalidateToken(Guid token);
}