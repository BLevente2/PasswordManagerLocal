using PasswordManagerLocal.Runtime.Abstractions;

namespace PasswordManagerLocal.Backend.Hosting;

public interface IBackendRuntimeLease : IAsyncDisposable
{
    BackendLifetimeReason Reason { get; }
}
