using PasswordManagerLocal.Backend.Hosting;
using PasswordManagerLocal.Runtime.Abstractions;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class FakeAgentBackendLifetimeCoordinator : IBackendRuntimeLifetimeCoordinator
{
    public BackendLifetimeReason ActiveReasons { get; set; }

    public Task<IBackendRuntimeLease> AcquireAsync(
        BackendLifetimeReason reason,
        CancellationToken cancellationToken = default) =>
        Task.FromException<IBackendRuntimeLease>(
            new NotSupportedException("This fake is used for owner lifecycle tests."));

    public Task<IBackendRuntimeLease> ResetDatabaseAndAcquireAsync(
        BackendLifetimeReason reason,
        CancellationToken cancellationToken = default) =>
        Task.FromException<IBackendRuntimeLease>(
            new NotSupportedException("This fake is used for owner lifecycle tests."));

    public Task RecoverRuntimeAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
