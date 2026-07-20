namespace PasswordManagerLocal.Runtime.Abstractions;

public interface IFrontendBackendClient : IAsyncDisposable
{
    BackendRuntimeSnapshot Snapshot { get; }

    event EventHandler<BackendRuntimeStateChangedEventArgs>? StateChanged;

    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task WaitUntilReadyAsync(CancellationToken cancellationToken = default);
    Task ResetDatabaseAndRestartAsync(CancellationToken cancellationToken = default);
}

public interface IFrontendBackendClient<TEndpoints> : IFrontendBackendClient
    where TEndpoints : class
{
    Task<TEndpoints> GetEndpointsAsync(CancellationToken cancellationToken = default);
}
