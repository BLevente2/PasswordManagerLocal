using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IIncomingDeltaApplierService
{
    Task<long> ApplyAsync(NetworkDelta delta, CancellationToken ct = default);
}
