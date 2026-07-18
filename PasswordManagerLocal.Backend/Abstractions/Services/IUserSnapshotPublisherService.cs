using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserSnapshotPublisherService
{
    Task<UserSyncSnapshot> GetOrCreateAsync(User user, CancellationToken ct = default);
    Task<UserSyncSnapshot?> GetLatestAsync(Guid userId, long userKeyEpoch, CancellationToken ct = default);
}
