using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Runtime.Abstractions;

namespace PasswordManagerLocal.Backend.Hosting;

public sealed class InProcessFrontendBackendClient : IFrontendBackendClient<IEndpoints>
{
    private readonly IBackendRuntime _backendRuntime;

    public InProcessFrontendBackendClient(IBackendRuntime backendRuntime)
    {
        _backendRuntime = backendRuntime ?? throw new ArgumentNullException(nameof(backendRuntime));
    }

    public BackendRuntimeSnapshot Snapshot => _backendRuntime.Snapshot;

    public event EventHandler<BackendRuntimeStateChangedEventArgs>? StateChanged
    {
        add => _backendRuntime.StateChanged += value;
        remove => _backendRuntime.StateChanged -= value;
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        _backendRuntime.EnsureStartedAsync(cancellationToken);

    public Task WaitUntilReadyAsync(CancellationToken cancellationToken = default) =>
        _backendRuntime.WaitUntilReadyAsync(cancellationToken);

    public Task ResetDatabaseAndRestartAsync(CancellationToken cancellationToken = default) =>
        _backendRuntime.ResetDatabaseAndRestartAsync(cancellationToken);

    public Task<IEndpoints> GetEndpointsAsync(CancellationToken cancellationToken = default) =>
        _backendRuntime.GetEndpointsAsync(cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
