using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Abstractions.Repositories;

public interface IUserRevisionKnowledgeRepository
{
    Task<UserRevisionKnowledge?> GetAsync(Guid userId, Guid originDeviceId, Guid originInstanceId, long userKeyEpoch, CancellationToken ct = default);
    Task<IReadOnlyList<UserRevisionKnowledge>> ListAsync(Guid userId, long userKeyEpoch, CancellationToken ct = default);
    Task AddAsync(UserRevisionKnowledge knowledge, CancellationToken ct = default);
    void Update(UserRevisionKnowledge knowledge);
}
