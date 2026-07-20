using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Backend.Hosting;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeInteractiveBackendSession : IInteractiveBackendSession
{
    private readonly Action? _onDispose;
    private IEndpoints? _endpoints;

    public FakeInteractiveBackendSession(IEndpoints endpoints, Action? onDispose = null)
    {
        _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
        _onDispose = onDispose;
    }

    public IEndpoints Endpoints => _endpoints
        ?? throw new ObjectDisposedException(nameof(FakeInteractiveBackendSession));

    public ValueTask DisposeAsync()
    {
        if (_endpoints is null)
            return ValueTask.CompletedTask;

        _endpoints = null;
        _onDispose?.Invoke();
        return ValueTask.CompletedTask;
    }
}
