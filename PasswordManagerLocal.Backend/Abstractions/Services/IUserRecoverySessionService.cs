using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserRecoverySessionService
{
    Task RefreshOrInvalidateAsync(User user, CancellationToken ct = default);
}
