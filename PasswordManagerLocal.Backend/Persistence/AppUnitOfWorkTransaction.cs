using Microsoft.EntityFrameworkCore.Storage;
using PasswordManagerLocal.Backend.Abstractions.Persistence;

namespace PasswordManagerLocal.Backend.Persistence;

internal sealed class AppUnitOfWorkTransaction : IUnitOfWorkTransaction
{
    private readonly IDbContextTransaction _transaction;

    public AppUnitOfWorkTransaction(IDbContextTransaction transaction)
    {
        _transaction = transaction;
    }

    public Task CommitAsync(CancellationToken ct = default) =>
        _transaction.CommitAsync(ct);

    public Task RollbackAsync(CancellationToken ct = default) =>
        _transaction.RollbackAsync(ct);

    public ValueTask DisposeAsync() =>
        _transaction.DisposeAsync();
}
