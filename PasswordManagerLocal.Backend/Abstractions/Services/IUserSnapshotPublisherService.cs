using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserSnapshotPublisherService
{
    Task<UserSyncSnapshot> GetOrCreateAsync(User user, CancellationToken ct = default);
    Task<UserSyncSnapshot?> GetLatestAsync(Guid userId, long userKeyEpoch, CancellationToken ct = default);
    Task<UserSyncSnapshot> GetOrCreateAfterRecoveryAsync(
        User user,
        EncryptionKey key,
        UserSyncKeyConfidence keyConfidence,
        CancellationToken ct = default) =>
        GetOrCreateAsync(user, ct);
}
