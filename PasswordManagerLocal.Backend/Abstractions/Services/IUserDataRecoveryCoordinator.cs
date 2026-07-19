using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserDataRecoveryCoordinator
{
    Task<UserDataRecoveryResult> TryRecoverAsync(
        Guid userId,
        EncryptionKey key,
        UserSyncKeyConfidence keyConfidence,
        UserDataRecoveryTrigger trigger,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a unique password salt only when it is carried by authenticated, historically
    /// authorized current-epoch recovery evidence. Ambiguous or unavailable evidence returns null.
    /// </summary>
    Task<byte[]?> TryResolvePasswordSaltAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult<byte[]?>(null);
}
