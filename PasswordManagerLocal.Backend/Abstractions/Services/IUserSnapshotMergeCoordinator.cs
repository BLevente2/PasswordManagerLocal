using PasswordManagerLocal.Backend.Security;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserSnapshotMergeCoordinator
{
    Task<bool> TryMergePendingAsync(Guid userId, EncryptionKey key, CancellationToken ct = default);
    Task<bool> TryMergePendingUnderLifecycleAsync(Guid userId, EncryptionKey key, CancellationToken ct = default);
}
