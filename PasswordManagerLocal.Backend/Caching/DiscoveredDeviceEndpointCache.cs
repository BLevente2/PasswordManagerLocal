using PasswordManagerLocal.Backend.Sync;
using System.Collections.Concurrent;
using PasswordManagerLocal.Backend.Utils;
using PasswordManagerLocal.Backend.Abstractions.Caching;

namespace PasswordManagerLocal.Backend.Caching;

public sealed class DiscoveredDeviceEndpointCache : IDiscoveredDeviceEndpointCache
{
    private readonly ConcurrentDictionary<string, DiscoveredDeviceEndpoint> _endpointsByFingerprint = new(StringComparer.OrdinalIgnoreCase);




    public void AddOrUpdate(DiscoveredDeviceEndpoint endpoint)
    {
        if (endpoint is null)
            return;

        var fingerprint = FingerprintUtil.NormalizeOrEmpty(endpoint.TlsCertFingerprint);
        if (fingerprint.Length == 0 || string.IsNullOrWhiteSpace(endpoint.Host) || endpoint.Port <= 0)
            return;

        _endpointsByFingerprint[fingerprint] = Clone(endpoint);
    }


    public bool TryGetByFingerprint(string tlsFingerprint, out DiscoveredDeviceEndpoint? endpoint)
    {
        var fingerprint = FingerprintUtil.NormalizeOrEmpty(tlsFingerprint);
        if (fingerprint.Length == 0 || !_endpointsByFingerprint.TryGetValue(fingerprint, out var cachedEndpoint))
        {
            endpoint = null;
            return false;
        }

        endpoint = Clone(cachedEndpoint);
        return true;
    }


    public bool TryRemove(string tlsFingerprint)
    {
        var fingerprint = FingerprintUtil.NormalizeOrEmpty(tlsFingerprint);
        return fingerprint.Length != 0 && _endpointsByFingerprint.TryRemove(fingerprint, out _);
    }


    public void Clear() =>
        _endpointsByFingerprint.Clear();


    private DiscoveredDeviceEndpoint Clone(DiscoveredDeviceEndpoint endpoint) =>
        new()
        {
            Host = endpoint.Host,
            Port = endpoint.Port,
            TlsCertFingerprint = endpoint.TlsCertFingerprint
        };


}
