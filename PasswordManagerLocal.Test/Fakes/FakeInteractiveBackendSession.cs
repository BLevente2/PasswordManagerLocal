using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Backend.Hosting;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeInteractiveBackendSession : IInteractiveBackendSession
{
    private readonly Action? _onDispose;
    private readonly Func<Exception?>? _disposeFailure;
    private IEndpoints? _endpoints;
    private Task? _disposeTask;

    public FakeInteractiveBackendSession(
        IEndpoints endpoints,
        Action? onDispose = null,
        Func<Exception?>? disposeFailure = null)
    {
        _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
        _onDispose = onDispose;
        _disposeFailure = disposeFailure;
    }

    public IEndpoints Endpoints => _endpoints
        ?? throw new ObjectDisposedException(nameof(FakeInteractiveBackendSession));

    public ValueTask DisposeAsync()
    {
        _disposeTask ??= DisposeCoreAsync();
        return new ValueTask(_disposeTask);
    }

    private Task DisposeCoreAsync()
    {
        if (_endpoints is null)
            return Task.CompletedTask;

        _endpoints = null;
        _onDispose?.Invoke();
        return _disposeFailure?.Invoke() is { } failure
            ? Task.FromException(failure)
            : Task.CompletedTask;
    }
}
