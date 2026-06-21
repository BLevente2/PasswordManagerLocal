using Microsoft.EntityFrameworkCore;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Persistence;

namespace PasswordManagerLocalBackend.Repositories;

public sealed class LocalUserDeviceRepository : ILocalUserDeviceRepository
{
    private readonly DbSet<LocalUserDevice> _set;

    public LocalUserDeviceRepository(AppDbContext context)
    {
        _set = context.LocalUserDevices;
    }

    public Task<LocalUserDevice?> GetAsync(Guid userId, CancellationToken ct = default) =>
        _set.Include(x => x.LocalDeviceIdentity).FirstOrDefaultAsync(x => x.UserId == userId, ct);

    public async Task<bool> IsSyncOnAsync(Guid userId, CancellationToken ct = default)
    {
        var link = await _set.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
        return link?.IsSyncOn == true;
    }

    public async Task<bool> AnySyncOnAsync(CancellationToken ct = default)
    {
        var links = await _set.AsNoTracking().ToListAsync(ct);
        return links.Any(x => x.IsSyncOn);
    }

    public async Task<IReadOnlyList<Guid>> ListSyncOnUserIdsAsync(CancellationToken ct = default)
    {
        var links = await _set.AsNoTracking().ToListAsync(ct);
        return links.Where(x => x.IsSyncOn).Select(x => x.UserId).Distinct().ToList();
    }

    public Task AddAsync(LocalUserDevice localUserDevice, CancellationToken ct = default) =>
        _set.AddAsync(localUserDevice, ct).AsTask();

    public void Update(LocalUserDevice localUserDevice) =>
        _set.Update(localUserDevice);

    public void Delete(LocalUserDevice localUserDevice) =>
        _set.Remove(localUserDevice);
}
