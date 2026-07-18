namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserAccountDeletionCleanupService
{
    Task DeleteCanonicalAndPendingStateAsync(Guid userId, CancellationToken ct = default);
}
