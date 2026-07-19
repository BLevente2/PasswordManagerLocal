using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IDatabaseHealthService
{
    Task<DatabaseHealthCheckResult> CheckAsync(CancellationToken ct = default);
}
