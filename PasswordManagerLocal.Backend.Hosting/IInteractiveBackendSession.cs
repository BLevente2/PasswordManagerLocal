using PasswordManagerLocal.Contracts.Endpoints;

namespace PasswordManagerLocal.Backend.Hosting;

public interface IInteractiveBackendSession : IAsyncDisposable
{
    IEndpoints Endpoints { get; }
    bool AcceptsNewOperations { get; }
    bool IsClosing { get; }
    int ActiveOperationCount { get; }
}
