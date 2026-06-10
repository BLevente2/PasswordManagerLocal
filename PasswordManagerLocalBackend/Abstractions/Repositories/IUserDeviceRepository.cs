using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Abstractions.Repositories;

public interface IUserDeviceRepository
{
    Task<IReadOnlyList<UserDevice>> ListByUserAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<UserDevice>> ListByDeviceAsync(Guid deviceId, CancellationToken ct = default);
    Task<IReadOnlyList<UserDevice>> ListActiveByDeviceAsync(Guid deviceId, CancellationToken ct = default);
    Task<UserDevice?> GetAsync(Guid userId, Guid deviceId, CancellationToken ct = default);
    Task<UserDevice?> GetByModelIdAsync(Guid modelId, CancellationToken ct = default);
    Task<UserDevice?> GetActiveByNameAsync(Guid userId, string name, Guid? exceptDeviceId = null, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid userId, Guid deviceId, CancellationToken ct = default);
    Task<bool> HasActiveLinkAsync(Guid userId, Guid deviceId, CancellationToken ct = default);
    Task<bool> HasAnyActiveLinkForDeviceAsync(Guid deviceId, CancellationToken ct = default);
    Task<bool> HasAnyActiveSyncEnabledLinkForDeviceAsync(Guid deviceId, CancellationToken ct = default);
    Task<bool> HasAnyActiveLinkForDeviceExceptUserAsync(Guid deviceId, Guid userId, CancellationToken ct = default);
    Task<bool> SharesActiveUserAsync(Guid sourceDeviceId, Guid targetDeviceId, CancellationToken ct = default);
    Task<bool> IsNameTakenAsync(Guid userId, string name, Guid? exceptDeviceId = null, CancellationToken ct = default);
    Task AddAsync(UserDevice userDevice, CancellationToken ct = default);
    void Update(UserDevice userDevice);
    void Delete(UserDevice userDevice);
}
