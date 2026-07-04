using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserDeltaApplierService
{
    Task<bool> ApplyAsync(SyncDeltaPayload payload, Guid sourceDeviceId, long ts, CancellationToken ct = default);
}
