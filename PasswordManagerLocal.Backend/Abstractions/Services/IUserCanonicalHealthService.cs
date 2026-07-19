using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserCanonicalHealthService
{
    Task<CanonicalHealthResult> VerifyAsync(
        User user,
        EncryptionKey? key,
        UserSyncKeyConfidence keyConfidence,
        bool recordFault,
        CancellationToken ct = default);

    Task UpdateCheckpointAsync(User user, CancellationToken ct = default);
    Task DeleteCheckpointAsync(Guid userId, CancellationToken ct = default);
}
