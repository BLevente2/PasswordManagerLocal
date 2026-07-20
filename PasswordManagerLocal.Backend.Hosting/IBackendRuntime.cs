using PasswordManagerLocal.Runtime.Abstractions;
using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Hosting;

public interface IBackendRuntime : IAsyncDisposable
{
    BackendRuntimeSnapshot Snapshot { get; }
    SyncRuntimeSnapshot SyncSnapshot { get; }

    event EventHandler<BackendRuntimeStateChangedEventArgs>? StateChanged;
    event EventHandler<SyncRuntimeStateChangedEventArgs>? SyncStateChanged;

    Task EnsureStartedAsync(CancellationToken cancellationToken = default);
    Task WaitUntilReadyAsync(CancellationToken cancellationToken = default);
    Task<IEndpoints> GetEndpointsAsync(CancellationToken cancellationToken = default);
    Task ResetDatabaseAndRestartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
