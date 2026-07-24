using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Abstractions.Sync.Discovery;

namespace PasswordManagerLocal.Backend.Services;

public sealed class DeviceOnlineStatusEvaluator
{
    private readonly IBackendExecutionProfileProvider _executionProfileProvider;
    private readonly IDiscoveredDeviceEndpointRegistry _endpointRegistry;

    public DeviceOnlineStatusEvaluator(
        IBackendExecutionProfileProvider executionProfileProvider,
        IDiscoveredDeviceEndpointRegistry endpointRegistry)
    {
        _executionProfileProvider = executionProfileProvider
            ?? throw new ArgumentNullException(nameof(executionProfileProvider));
        _endpointRegistry = endpointRegistry
            ?? throw new ArgumentNullException(nameof(endpointRegistry));
    }

    public bool IsRecentlyDiscovered(string tlsFingerprint)
    {
        var profile = _executionProfileProvider.Current;
        return profile is not null &&
            _endpointRegistry.IsRecentlyDiscovered(
                tlsFingerprint,
                profile.DeviceOnlineTimeout);
    }
}
