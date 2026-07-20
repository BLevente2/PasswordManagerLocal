using PasswordManagerLocal.Backend.Abstractions;

namespace PasswordManagerLocal.Backend.Hosting;

public interface IInteractiveBackendSession : IAsyncDisposable
{
    IEndpoints Endpoints { get; }
}
