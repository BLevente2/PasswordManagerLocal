using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Sync;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IDeviceSyncTaskService
{
    bool TryStart(DiscoveredDeviceEndpoint endpoint, Device device);
    Task StopAllAsync(CancellationToken ct = default);
}
