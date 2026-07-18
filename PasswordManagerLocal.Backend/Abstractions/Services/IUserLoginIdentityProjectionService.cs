using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserLoginIdentityProjectionService
{
    Task<UserLoginIdentityState> SetCanonicalAsync(
        User user,
        SyncVersionStamp generalUserDataVersion,
        CancellationToken ct = default);

    Task<UserLoginIdentityState?> RecalculateAsync(Guid userId, CancellationToken ct = default);
    Task<UserLoginIdentityState?> RecalculateUnderLifecycleAsync(Guid userId, CancellationToken ct = default);

    Task<UserLoginIdentityMatchResult> FindByUsernameAsync(
        byte[] normalizedUsername,
        CancellationToken ct = default);
}
