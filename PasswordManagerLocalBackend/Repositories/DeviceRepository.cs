using Microsoft.EntityFrameworkCore;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Persistence;

namespace PasswordManagerLocalBackend.Repositories;

public sealed class DeviceRepository : GenericRepositoryBase<Device>, IDeviceRepository
{
    private readonly AppDbContext _context;

    public DeviceRepository(AppDbContext context) : base(context.Devices)
    {
        _context = context;
    }

    public override Task<Device?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<IReadOnlyList<Device>> ListDevicesNeedingSyncAsync(CancellationToken ct = default)
    {
        var devices = await Set.AsNoTracking()
            .Where(d => d.IsTrusted && !d.IsBlocked && d.TlsCertFingerprint != string.Empty && d.ItemsNeedingSync.Any(q => q.ProcessedAt == null))
            .ToListAsync(ct);
        return devices.Where(d => d.PublicKey.Length != 0 && d.SignPublicKey.Length != 0).ToList();
    }

    public async Task<IReadOnlyList<Device>> ListUserDevicesAsync(Guid uid, CancellationToken ct = default)
    {
        if (!await _context.LocalUserDevices.AsNoTracking().AnyAsync(x => x.UserId == uid && x.IsSyncOn, ct))
            return [];
        return await Set.AsNoTracking()
            .Where(d => d.UserDevices.Any(ud => ud.UserId == uid && !ud.IsDeleted && ud.IsSyncOn))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Device>> ListGroupDevicesAsync(Guid groupId, CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Where(d => d.UserDevices.Any(ud =>
                !ud.IsDeleted && ud.IsSyncOn &&
                _context.LocalUserDevices.Any(l => l.UserId == ud.UserId && l.IsSyncOn) &&
                ud.User!.Groups.Any(g => g.Id == groupId)))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Device>> ListDevicesLinkedToDeviceUsersAsync(Guid deviceId, CancellationToken ct = default)
    {
        var userIds = await _context.UserDevices.AsNoTracking()
            .Where(ud => ud.DeviceId == deviceId && !ud.IsDeleted && _context.LocalUserDevices.Any(l => l.UserId == ud.UserId && l.IsSyncOn))
            .Select(ud => ud.UserId)
            .ToListAsync(ct);
        if (userIds.Count == 0)
            return [];
        return await Set.AsNoTracking()
            .Where(d => d.Id != deviceId && d.UserDevices.Any(ud => userIds.Contains(ud.UserId) && !ud.IsDeleted && ud.IsSyncOn))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Device>> ListUserDeviceChangeTargetDevicesAsync(Guid userId, Guid changedDeviceId, bool includeChangedDevice, CancellationToken ct = default)
    {
        if (!await _context.LocalUserDevices.AsNoTracking().AnyAsync(x => x.UserId == userId && x.IsSyncOn, ct))
            return [];
        return await Set.AsNoTracking()
            .Where(d => d.UserDevices.Any(ud =>
                ud.UserId == userId &&
                !ud.IsDeleted &&
                ((d.Id != changedDeviceId && ud.IsSyncOn) || (includeChangedDevice && d.Id == changedDeviceId))))
            .ToListAsync(ct);
    }

    public Task<Device?> GetByIdWithUsersAsync(Guid id, CancellationToken ct = default) =>
        Set.Include(d => d.UserDevices).ThenInclude(ud => ud.User).FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<Device?> GetByIdAsNoTrackingWithUsersAsync(Guid id, CancellationToken ct = default) =>
        Set.AsNoTracking().Include(d => d.UserDevices).ThenInclude(ud => ud.User).FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<Device?> GetByIdWithUserDevicesAsync(Guid id, CancellationToken ct = default) =>
        Set.Include(d => d.UserDevices).FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<Device?> GetBySignPublicKeyAsync(byte[] signPublicKey, CancellationToken ct = default)
    {
        if (signPublicKey.Length == 0) return null;
        var devices = await Set.ToListAsync(ct);
        return devices.FirstOrDefault(d => d.SignPublicKey.SequenceEqual(signPublicKey));
    }

    public async Task<Device?> GetByTlsCertFingerprintAsync(string tlsCertFingerprint, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tlsCertFingerprint)) return null;
        var normalized = NormalizeFingerprint(tlsCertFingerprint);
        var devices = await Set.ToListAsync(ct);
        return devices.FirstOrDefault(d => NormalizeFingerprint(d.TlsCertFingerprint) == normalized);
    }

    public async Task<Device?> GetByTlsCertFingerprintWithUserDevicesAsync(string tlsCertFingerprint, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tlsCertFingerprint)) return null;
        var normalized = NormalizeFingerprint(tlsCertFingerprint);
        var devices = await Set.Include(d => d.UserDevices).ToListAsync(ct);
        return devices.FirstOrDefault(d => NormalizeFingerprint(d.TlsCertFingerprint) == normalized);
    }

    public async Task<IReadOnlyList<Device>> ListLocalSelfDevicesAsync(Guid localDeviceId, byte[] signPublicKey, string tlsCertFingerprint, CancellationToken ct = default)
    {
        var normalizedFingerprint = NormalizeFingerprint(tlsCertFingerprint);
        var devices = await Set.Include(d => d.UserDevices).ToListAsync(ct);
        return devices.Where(d => d.Id == localDeviceId ||
            (signPublicKey.Length != 0 && d.SignPublicKey.SequenceEqual(signPublicKey)) ||
            (normalizedFingerprint.Length != 0 && NormalizeFingerprint(d.TlsCertFingerprint) == normalizedFingerprint)).ToList();
    }

    private static string NormalizeFingerprint(string fingerprint) =>
        fingerprint.Replace(":", string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();
}
