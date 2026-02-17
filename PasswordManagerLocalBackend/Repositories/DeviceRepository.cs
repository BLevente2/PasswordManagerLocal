using Microsoft.EntityFrameworkCore;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Persistence;

namespace PasswordManagerLocalBackend.Repositories;

public sealed class DeviceRepository : GenericRepositoryBase<Device>, IDeviceRepository
{
    public DeviceRepository(AppDbContext context) : base(context.Devices) { }

    public override async Task<Device?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<IReadOnlyList<Device>> ListDevicesNeedingSyncAsync(CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Where(d => !d.IsBlocked && d.ItemsNeedingSync.Any(q => q.ProcessedAt == null))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Device>> ListUserDevicesAsync(Guid uid, CancellationToken ct = default) =>
        await Set.AsNoTracking()
        .Where(d => d.Users.Any(u => u.UId == uid) && !d.IsBlocked)
        .ToListAsync(ct);

    public async Task<IReadOnlyList<Device>> ListGroupDevicesAsync(Guid groupId, CancellationToken ct = default) =>
        await Set.AsNoTracking()
        .Where(d => d.Users.Any(u => u.Groups.Any(g => g.Id == groupId)) && !d.IsBlocked)
        .ToListAsync(ct);
}