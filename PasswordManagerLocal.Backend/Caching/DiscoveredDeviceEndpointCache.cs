using PasswordManagerLocal.Backend.Sync;
using System.Collections.Concurrent;
using PasswordManagerLocal.Backend.Utils;
using PasswordManagerLocal.Backend.Abstractions.Caching;

namespace PasswordManagerLocal.Backend.Caching;

public sealed class DiscoveredDeviceEndpointCache : IDiscoveredDeviceEndpointCache
{
    private readonly ConcurrentDictionary<string, CachedEndpoint> _endpointsByFingerprint = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<DateTimeOffset> _utcNow;

    public DiscoveredDeviceEndpointCache()
        : this(static () => DateTimeOffset.UtcNow)
    {
    }

    internal DiscoveredDeviceEndpointCache(Func<DateTimeOffset> utcNow)
    {
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }

    public void AddOrUpdate(DiscoveredDeviceEndpoint endpoint)
    {
        if (endpoint is null)
            return;

        var fingerprint = FingerprintUtil.NormalizeOrEmpty(endpoint.TlsCertFingerprint);
        if (fingerprint.Length == 0 || string.IsNullOrWhiteSpace(endpoint.Host) || endpoint.Port <= 0)
            return;

        _endpointsByFingerprint[fingerprint] = new CachedEndpoint(Clone(endpoint), _utcNow());
    }

    public bool TryGetByFingerprint(string tlsFingerprint, out DiscoveredDeviceEndpoint? endpoint)
    {
        var fingerprint = FingerprintUtil.NormalizeOrEmpty(tlsFingerprint);
        if (fingerprint.Length == 0 || !_endpointsByFingerprint.TryGetValue(fingerprint, out var cachedEndpoint))
        {
            endpoint = null;
            return false;
        }

        endpoint = Clone(cachedEndpoint.Endpoint);
        return true;
    }

    public bool IsRecentlyDiscovered(string tlsFingerprint, TimeSpan maximumAge)
    {
        if (maximumAge <= TimeSpan.Zero)
            return false;

        var fingerprint = FingerprintUtil.NormalizeOrEmpty(tlsFingerprint);
        if (fingerprint.Length == 0 || !_endpointsByFingerprint.TryGetValue(fingerprint, out var cachedEndpoint))
            return false;

        var age = _utcNow() - cachedEndpoint.ObservedAt;
        return age >= TimeSpan.Zero && age <= maximumAge;
    }

    public bool TryRemove(string tlsFingerprint)
    {
        var fingerprint = FingerprintUtil.NormalizeOrEmpty(tlsFingerprint);
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

    private sealed record CachedEndpoint(DiscoveredDeviceEndpoint Endpoint, DateTimeOffset ObservedAt);
}
