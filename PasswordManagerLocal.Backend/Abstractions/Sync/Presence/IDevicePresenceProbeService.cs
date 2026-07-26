using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync.Discovery;
using PasswordManagerLocal.Backend.Sync.Presence;

namespace PasswordManagerLocal.Backend.Abstractions.Sync.Presence;

public interface IDevicePresenceProbeService
{
    Task<DevicePresenceProbeResult> ProbeAsync(
        Device device,
        bool force = false,
        CancellationToken cancellationToken = default);

    void OnEndpointDiscovered(
        Device device,
        DiscoveredDeviceEndpoint endpoint);
}
