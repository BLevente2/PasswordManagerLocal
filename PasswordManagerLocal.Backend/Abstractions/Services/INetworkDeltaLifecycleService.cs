using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface INetworkDeltaLifecycleService
{
    Task<bool> TryAcknowledgeAlreadyDeletedUserAsync(SyncDeltaPayload payload, Device sourceDevice, long ts, CancellationToken ct = default);
    Task TouchSourceDeviceAsync(Device sourceDevice, CancellationToken ct = default);
    Task BeforeSaveAsync(SyncDeltaPayload payload, Device sourceDevice, bool applied, long ts, CancellationToken ct = default);
    Task AfterSaveAsync(SyncDeltaPayload payload, Device sourceDevice, bool applied, long ts, CancellationToken ct = default);
}
