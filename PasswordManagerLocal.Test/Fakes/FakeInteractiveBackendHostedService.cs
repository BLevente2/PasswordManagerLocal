using PasswordManagerLocal.Backend.Abstractions.Services;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeInteractiveBackendHostedService : IInteractiveBackendHostedService
{
    public int StartCalls { get; private set; }
    public int StopCalls { get; private set; }
    public Exception? StartFailure { get; set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartCalls++;
        if (StartFailure is not null)
            throw StartFailure;

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StopCalls++;
        return Task.CompletedTask;
    }
}
