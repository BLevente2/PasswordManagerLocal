using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Abstractions.Repositories;

public interface IUserRepository : IGenericRepository<User>
{
    Task<IReadOnlyList<User>> GetAllRememberMeEnabledUsersAsync(CancellationToken ct = default);
    Task<User?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByIdWithRelationsAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByIdAsNoTrackingWithRelationsAsync(Guid id, CancellationToken ct = default);
}