using PasswordManagerLocalBackend.Sync;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public class IIncomingDeltaApplierService
{
    Task<long> ApplyAsync(NetworkDelta delta, CancellationToken ct = default);
}