using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Abstractions.Repositories;

public interface IUserCanonicalCheckpointRepository
{
    Task<UserCanonicalCheckpoint?> GetAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(UserCanonicalCheckpoint checkpoint, CancellationToken ct = default);
    void Update(UserCanonicalCheckpoint checkpoint);
    void Delete(UserCanonicalCheckpoint checkpoint);
}
