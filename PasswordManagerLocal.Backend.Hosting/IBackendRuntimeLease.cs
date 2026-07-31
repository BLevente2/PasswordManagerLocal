using PasswordManagerLocal.Contracts.Runtime;
using PasswordManagerLocal.Contracts.BackgroundSync;

namespace PasswordManagerLocal.Backend.Hosting;

public interface IBackendRuntimeLease : IAsyncDisposable
{
    BackendLifetimeReason Reason { get; }
}
