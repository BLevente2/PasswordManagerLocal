using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Sync;
using System.Collections.Concurrent;

namespace PasswordManagerLocalBackend.Services;

public sealed class DiscoveredDeviceEndpointCache : IDiscoveredDeviceEndpointCache
{
    private readonly ConcurrentDictionary<string, DiscoveredDeviceEndpoint> _endpointsByFingerprint = new(StringComparer.OrdinalIgnoreCase);




    public void AddOrUpdate(DiscoveredDeviceEndpoint endpoint)
    {
        if (endpoint is null)
            return;

        var fingerprint = NormalizeFingerprint(endpoint.TlsCertFingerprint);
        if (fingerprint.Length == 0 || string.IsNullOrWhiteSpace(endpoint.Host) || endpoint.Port <= 0)
            return;

        _endpointsByFingerprint[fingerprint] = Clone(endpoint);
    }


    public bool TryGetByFingerprint(string tlsFingerprint, out DiscoveredDeviceEndpoint? endpoint)
    {
        var fingerprint = NormalizeFingerprint(tlsFingerprint);
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
        var fingerprint = NormalizeFingerprint(tlsFingerprint);
        return fingerprint.Length != 0 && _endpointsByFingerprint.TryRemove(fingerprint, out _);
    }


    public void Clear() =>
        _endpointsByFingerprint.Clear();


    private static DiscoveredDeviceEndpoint Clone(DiscoveredDeviceEndpoint endpoint) =>
        new()
        {
            Host = endpoint.Host,
            Port = endpoint.Port,
            TlsCertFingerprint = endpoint.TlsCertFingerprint
        };


    private static string NormalizeFingerprint(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
            return string.Empty;

        var s = fingerprint.Trim();
        s = s.Replace(":", string.Empty);
        s = s.Replace(" ", string.Empty);
        return s.ToUpperInvariant();
    }
}
