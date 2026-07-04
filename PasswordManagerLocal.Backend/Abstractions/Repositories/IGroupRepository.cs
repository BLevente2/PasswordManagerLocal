using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Abstractions.Repositories;

public interface IGroupRepository : IGenericRepository<Group>
{
    Task<IReadOnlyList<Group>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task<Group?> GetByIdWithUsersAsync(Guid id, CancellationToken ct = default);
    Task<Group?> GetByIdAsNoTrackingWithUsersAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Group>> ListByUserWithUsersAsNoTrackingAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> ListIdsByUserAsync(Guid userId, CancellationToken ct = default);
}