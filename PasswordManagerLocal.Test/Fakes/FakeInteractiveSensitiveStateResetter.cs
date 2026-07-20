using PasswordManagerLocal.Backend.Abstractions.Services;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeInteractiveSensitiveStateResetter : IInteractiveSensitiveStateResetter
{
    public int ResetCalls { get; private set; }

    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResetCalls++;
        return Task.CompletedTask;
    }
}
