using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Abstractions.Repositories;

public interface IUserRepository : IGenericRepository<User>
{
    Task<IReadOnlyList<UserLoginLookupData>> ListLoginLookupDataAsync(CancellationToken ct = default);
    Task<IReadOnlyList<User>> GetAllRememberMeEnabledUsersAsync(CancellationToken ct = default);
    Task<User?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<User>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task<User?> GetByIdWithRelationsAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByIdAsNoTrackingWithRelationsAsync(Guid id, CancellationToken ct = default);
    Task UpdateSavedKeyAsync(Guid id, byte[]? savedKey, CancellationToken ct = default);
}