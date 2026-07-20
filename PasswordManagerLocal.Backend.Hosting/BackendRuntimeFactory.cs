using PasswordManagerLocal.Backend.Abstractions.Security;
using PasswordManagerLocal.Backend.Abstractions.Sync.Discovery;
using PasswordManagerLocal.Backend.Configuration;

namespace PasswordManagerLocal.Backend.Hosting;

public static class BackendRuntimeFactory
{
    public static IBackendRuntime Create(
        BackendStoragePaths storagePaths,
        Func<IKeyProtector> keyProtectorFactory,
        Func<ILocalDiscoveryNetworkLease> discoveryNetworkLeaseFactory) =>
        new BackendRuntime(
            new BackendRuntimeOptions(
                storagePaths,
                keyProtectorFactory,
                discoveryNetworkLeaseFactory));
}
