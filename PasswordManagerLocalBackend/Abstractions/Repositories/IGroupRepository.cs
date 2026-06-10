using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Abstractions.Repositories;

public interface IGroupRepository : IGenericRepository<Group>
{
    Task<Group?> GetByIdWithUsersAsync(Guid id, CancellationToken ct = default);
    Task<Group?> GetByIdAsNoTrackingWithUsersAsync(Guid id, CancellationToken ct = default);
}