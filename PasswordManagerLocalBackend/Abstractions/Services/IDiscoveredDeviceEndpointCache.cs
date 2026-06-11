using PasswordManagerLocalBackend.Sync;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IDiscoveredDeviceEndpointCache
{
    void AddOrUpdate(DiscoveredDeviceEndpoint endpoint);
    bool TryGetByFingerprint(string tlsFingerprint, out DiscoveredDeviceEndpoint? endpoint);
    bool TryRemove(string tlsFingerprint);
    void Clear();
}
