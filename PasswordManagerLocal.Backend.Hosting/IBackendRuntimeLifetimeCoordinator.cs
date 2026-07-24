using PasswordManagerLocal.Runtime.Abstractions;

namespace PasswordManagerLocal.Backend.Hosting;

public interface IBackendRuntimeLifetimeCoordinator
{
    BackendLifetimeReason ActiveReasons { get; }
    event EventHandler? ActiveReasonsChanged;

    Task<IBackendRuntimeLease> AcquireAsync(
        BackendLifetimeReason reason,
        CancellationToken cancellationToken = default);
    Task<IBackendRuntimeLease> ResetDatabaseAndAcquireAsync(
        BackendLifetimeReason reason,
        CancellationToken cancellationToken = default);
    Task RecoverRuntimeAsync(CancellationToken cancellationToken = default);
}
