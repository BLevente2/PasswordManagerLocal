using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Sync.Discovery;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IDeviceSyncTaskService
{
    bool TryStart(DiscoveredDeviceEndpoint endpoint, Device device);
    Task WaitForIdleAsync(Guid deviceId, CancellationToken ct = default);
    Task StopAllAsync(CancellationToken ct = default);
}
