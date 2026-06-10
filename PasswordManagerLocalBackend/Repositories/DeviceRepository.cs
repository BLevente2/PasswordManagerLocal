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

    public async Task<IReadOnlyList<Device>> ListDevicesNeedingSyncAsync(CancellationToken ct = default)
    {
        var devices = await Set.AsNoTracking()
            .Where(d =>
                d.IsTrusted &&
                !d.IsBlocked &&
                d.TlsCertFingerprint != string.Empty &&
                d.ItemsNeedingSync.Any(q => q.ProcessedAt == null) &&
                d.UserDevices.Any(ud => !ud.IsDeleted && ud.IsSyncEnabled))
            .ToListAsync(ct);

        return devices
            .Where(d => d.PublicKey.Length != 0 && d.SignPublicKey.Length != 0)
            .ToList();
    }

    public async Task<IReadOnlyList<Device>> ListUserDevicesAsync(Guid uid, CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Where(d => d.UserDevices.Any(ud => ud.UserId == uid && !ud.IsDeleted && ud.IsSyncEnabled))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Device>> ListGroupDevicesAsync(Guid groupId, CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Where(d => d.UserDevices.Any(ud => !ud.IsDeleted && ud.IsSyncEnabled && ud.User!.Groups.Any(g => g.Id == groupId)))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Device>> ListDevicesLinkedToDeviceUsersAsync(Guid deviceId, CancellationToken ct = default)
    {
        var userIds = await Set.AsNoTracking()
            .Where(d => d.Id == deviceId)
            .SelectMany(d => d.UserDevices.Where(ud => !ud.IsDeleted).Select(ud => ud.UserId))
            .ToListAsync(ct);

        if (userIds.Count == 0)
            return [];

        return await Set.AsNoTracking()
            .Where(d => d.UserDevices.Any(ud => userIds.Contains(ud.UserId) && !ud.IsDeleted && ud.IsSyncEnabled))
            .ToListAsync(ct);
    }


    public async Task<IReadOnlyList<Device>> ListUserDeviceChangeTargetDevicesAsync(Guid userId, Guid changedDeviceId, CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Where(d => d.UserDevices.Any(ud =>
                ud.UserId == userId &&
                ((!ud.IsDeleted && ud.IsSyncEnabled) || ud.DeviceId == changedDeviceId)))
            .ToListAsync(ct);


    public async Task<Device?> GetByIdWithUsersAsync(Guid id, CancellationToken ct = default) =>
        await Set
            .Include(d => d.Users)
            .Include(d => d.UserDevices)
            .FirstOrDefaultAsync(d => d.Id == id, ct);


    public async Task<Device?> GetByIdAsNoTrackingWithUsersAsync(Guid id, CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Include(d => d.Users)
            .Include(d => d.UserDevices)
            .FirstOrDefaultAsync(d => d.Id == id, ct);


    public async Task<Device?> GetByIdWithUserDevicesAsync(Guid id, CancellationToken ct = default) =>
        await Set
            .Include(d => d.UserDevices)
            .FirstOrDefaultAsync(d => d.Id == id, ct);


    public async Task<Device?> GetBySignPublicKeyAsync(byte[] signPublicKey, CancellationToken ct = default)
    {
        if (signPublicKey.Length == 0)
            return null;

        var devices = await Set.ToListAsync(ct);
        return devices.FirstOrDefault(d => d.SignPublicKey.SequenceEqual(signPublicKey));
    }


    public async Task<Device?> GetByTlsCertFingerprintAsync(string tlsCertFingerprint, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tlsCertFingerprint))
            return null;

        var normalized = NormalizeFingerprint(tlsCertFingerprint);

        var devices = await Set.ToListAsync(ct);
        return devices.FirstOrDefault(d => NormalizeFingerprint(d.TlsCertFingerprint) == normalized);
    }


    public async Task<Device?> GetByTlsCertFingerprintWithUserDevicesAsync(string tlsCertFingerprint, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tlsCertFingerprint))
            return null;

        var normalized = NormalizeFingerprint(tlsCertFingerprint);

        var devices = await Set
            .Include(d => d.UserDevices)
            .ToListAsync(ct);

        return devices.FirstOrDefault(d => NormalizeFingerprint(d.TlsCertFingerprint) == normalized);
    }


    public async Task<IReadOnlyList<Device>> ListLocalSelfDevicesAsync(Guid localDeviceId, byte[] signPublicKey, string tlsCertFingerprint, CancellationToken ct = default)
    {
        var normalizedFingerprint = NormalizeFingerprint(tlsCertFingerprint);
        var devices = await Set
            .Include(d => d.UserDevices)
            .ToListAsync(ct);

        return devices
            .Where(d =>
                d.Id == localDeviceId ||
                (signPublicKey.Length != 0 && d.SignPublicKey.SequenceEqual(signPublicKey)) ||
                (normalizedFingerprint.Length != 0 && NormalizeFingerprint(d.TlsCertFingerprint) == normalizedFingerprint))
            .ToList();
    }


    private static string NormalizeFingerprint(string fingerprint) =>
        fingerprint.Replace(":", string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();
}
