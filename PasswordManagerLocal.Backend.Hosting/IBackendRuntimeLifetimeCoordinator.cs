using PasswordManagerLocal.Runtime.Abstractions;

namespace PasswordManagerLocal.Backend.Hosting;

public interface IBackendRuntimeLifetimeCoordinator
{
    BackendLifetimeReason ActiveReasons { get; }

    Task<IBackendRuntimeLease> AcquireAsync(
        BackendLifetimeReason reason,
        CancellationToken cancellationToken = default);
    Task<IBackendRuntimeLease> ResetDatabaseAndAcquireAsync(
        BackendLifetimeReason reason,
        CancellationToken cancellationToken = default);
}
