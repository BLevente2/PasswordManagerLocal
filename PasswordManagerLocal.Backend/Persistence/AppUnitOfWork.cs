using PasswordManagerLocal.Backend.Abstractions.Persistence;

namespace PasswordManagerLocal.Backend.Persistence;

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

    }
