using PasswordManagerLocalBackend.Sync;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IIncomingDeltaApplierService
{
    Task<long> ApplyAsync(NetworkDelta delta, CancellationToken ct = default);
}
