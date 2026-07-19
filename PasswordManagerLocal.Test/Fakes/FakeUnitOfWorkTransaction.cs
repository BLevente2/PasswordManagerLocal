using PasswordManagerLocal.Backend.Abstractions.Persistence;

namespace PasswordManagerLocal.Test.Fakes;

internal sealed class FakeUnitOfWorkTransaction : IUnitOfWorkTransaction
{
    public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
