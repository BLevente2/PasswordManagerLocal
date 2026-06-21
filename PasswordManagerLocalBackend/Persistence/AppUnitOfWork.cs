using Microsoft.EntityFrameworkCore.Storage;
using PasswordManagerLocalBackend.Abstractions.Persistence;

namespace PasswordManagerLocalBackend.Persistence;

public sealed class AppUnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;

    public AppUnitOfWork(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default) =>
        new AppUnitOfWorkTransaction(await _db.Database.BeginTransactionAsync(ct));

    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);

    private sealed class AppUnitOfWorkTransaction : IUnitOfWorkTransaction
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
}
