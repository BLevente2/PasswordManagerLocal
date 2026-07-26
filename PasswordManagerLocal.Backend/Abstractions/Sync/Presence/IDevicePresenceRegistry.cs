using PasswordManagerLocal.Backend.Sync.Discovery;
using PasswordManagerLocal.Backend.Sync.Presence;

namespace PasswordManagerLocal.Backend.Abstractions.Sync.Presence;

public interface IDevicePresenceRegistry
{
    void RefreshAuthenticated(
        string tlsCertificateFingerprint,
        DiscoveredDeviceEndpoint? endpoint,
        DevicePresenceObservationSource source);

    void RecordFailure(
        string tlsCertificateFingerprint,
        DiscoveredDeviceEndpoint? endpoint,
        DevicePresenceFailureKind failureKind);

    void HandleEndpointChanged(
        string tlsCertificateFingerprint,
        DiscoveredDeviceEndpoint endpoint);

    bool IsOnline(string tlsCertificateFingerprint, TimeSpan maximumAge);

    int ExpireStale(TimeSpan maximumAge);

    void InvalidateAll(string reason);

    bool TryGetSnapshot(
        string tlsCertificateFingerprint,
        out DevicePresenceSnapshot? snapshot);
}
