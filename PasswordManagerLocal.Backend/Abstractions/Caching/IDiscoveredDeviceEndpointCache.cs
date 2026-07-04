using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Backend.Abstractions.Caching;

public interface IDiscoveredDeviceEndpointCache
{
    void AddOrUpdate(DiscoveredDeviceEndpoint endpoint);
    bool TryGetByFingerprint(string tlsFingerprint, out DiscoveredDeviceEndpoint? endpoint);
    bool TryRemove(string tlsFingerprint);
    void Clear();
}
