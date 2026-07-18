using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Abstractions.Repositories;

public interface IUserSyncStateRepository
{
    Task<UserSyncState?> GetAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(UserSyncState state, CancellationToken ct = default);
    void Update(UserSyncState state);
}
