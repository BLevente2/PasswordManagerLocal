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
        var localLink = await _context.LocalUserDevices.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == uid, ct);
        if (localLink is null)
            return [];
        localLink.VerifyIntegrity();
        if (!localLink.IsSyncOn)
            return [];

        var links = await _context.UserDevices.AsNoTracking()
            .Include(ud => ud.Device)
            .Where(ud => ud.UserId == uid)
            .ToListAsync(ct);
        VerifyUserDeviceLinks(links);
        return links.Where(ud => !ud.IsDeleted && ud.IsSyncOn && ud.Device is not null)
            .Select(ud => ud.Device!)
            .DistinctBy(d => d.Id)
            .ToList();
    }

    public async Task<IReadOnlyList<Device>> ListGroupDevicesAsync(Guid groupId, CancellationToken ct = default)
    {
        var groupUserIds = await _context.Users.AsNoTracking()
            .Where(u => u.Groups.Any(g => g.Id == groupId))
            .Select(u => u.UId)
            .ToListAsync(ct);
        var enabledUserIds = await GetSyncEnabledLocalUserIdsAsync(groupUserIds, ct);
        if (enabledUserIds.Count == 0)
            return [];

        var links = await _context.UserDevices.AsNoTracking()
            .Include(ud => ud.Device)
            .Where(ud => enabledUserIds.Contains(ud.UserId))
            .ToListAsync(ct);
        VerifyUserDeviceLinks(links);
        return links.Where(ud => !ud.IsDeleted && ud.IsSyncOn && ud.Device is not null)
            .Select(ud => ud.Device!)
            .DistinctBy(d => d.Id)
            .ToList();
    }

    public async Task<IReadOnlyList<Device>> ListDevicesLinkedToDeviceUsersAsync(Guid deviceId, CancellationToken ct = default)
    {
        var sourceLinks = await _context.UserDevices.AsNoTracking()
            .Where(ud => ud.DeviceId == deviceId)
            .ToListAsync(ct);
        VerifyUserDeviceLinks(sourceLinks);
        var sourceUserIds = sourceLinks.Where(ud => !ud.IsDeleted).Select(ud => ud.UserId).Distinct().ToList();
        var enabledUserIds = await GetSyncEnabledLocalUserIdsAsync(sourceUserIds, ct);
        if (enabledUserIds.Count == 0)
            return [];

        var targetLinks = await _context.UserDevices.AsNoTracking()
            .Include(ud => ud.Device)
            .Where(ud => ud.DeviceId != deviceId && enabledUserIds.Contains(ud.UserId))
            .ToListAsync(ct);
        VerifyUserDeviceLinks(targetLinks);
        return targetLinks.Where(ud => !ud.IsDeleted && ud.IsSyncOn && ud.Device is not null)
            .Select(ud => ud.Device!)
            .DistinctBy(d => d.Id)
            .ToList();
    }

    public async Task<IReadOnlyList<Device>> ListUserDeviceChangeTargetDevicesAsync(Guid userId, Guid changedDeviceId, bool includeChangedDevice, CancellationToken ct = default)
    {
        var localLink = await _context.LocalUserDevices.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (localLink is null)
            return [];
        localLink.VerifyIntegrity();
        if (!localLink.IsSyncOn)
            return [];

        var links = await _context.UserDevices.AsNoTracking()
            .Include(ud => ud.Device)
            .Where(ud => ud.UserId == userId)
            .ToListAsync(ct);
        VerifyUserDeviceLinks(links);
        return links.Where(ud =>
                !ud.IsDeleted &&
                ud.Device is not null &&
                ((ud.DeviceId != changedDeviceId && ud.IsSyncOn) || (includeChangedDevice && ud.DeviceId == changedDeviceId)))
            .Select(ud => ud.Device!)
            .DistinctBy(d => d.Id)
            .ToList();
    }

    public async Task<Device?> GetByIdWithUsersAsync(Guid id, CancellationToken ct = default)
    {
        var device = await Set.Include(d => d.UserDevices).ThenInclude(ud => ud.User).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is not null)
            VerifyUserDeviceLinks(device.UserDevices);
        return device;
    }

    public async Task<Device?> GetByIdAsNoTrackingWithUsersAsync(Guid id, CancellationToken ct = default)
    {
        var device = await Set.AsNoTracking().Include(d => d.UserDevices).ThenInclude(ud => ud.User).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is not null)
            VerifyUserDeviceLinks(device.UserDevices);
        return device;
    }

    public async Task<Device?> GetByIdWithUserDevicesAsync(Guid id, CancellationToken ct = default)
    {
        var device = await Set.Include(d => d.UserDevices).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is not null)
            VerifyUserDeviceLinks(device.UserDevices);
        return device;
    }

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
        foreach (var device in devices)
            VerifyUserDeviceLinks(device.UserDevices);
        return devices.FirstOrDefault(d => NormalizeFingerprint(d.TlsCertFingerprint) == normalized);
    }

    public async Task<IReadOnlyList<Device>> ListLocalSelfDevicesAsync(Guid localDeviceId, byte[] signPublicKey, string tlsCertFingerprint, CancellationToken ct = default)
    {
        var normalizedFingerprint = NormalizeFingerprint(tlsCertFingerprint);
        var devices = await Set.Include(d => d.UserDevices).ToListAsync(ct);
        foreach (var device in devices)
            VerifyUserDeviceLinks(device.UserDevices);
        return devices.Where(d => d.Id == localDeviceId ||
            (signPublicKey.Length != 0 && d.SignPublicKey.SequenceEqual(signPublicKey)) ||
            (normalizedFingerprint.Length != 0 && NormalizeFingerprint(d.TlsCertFingerprint) == normalizedFingerprint)).ToList();
    }


    private async Task<HashSet<Guid>> GetSyncEnabledLocalUserIdsAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var links = await _context.LocalUserDevices.AsNoTracking()
            .Where(link => ids.Contains(link.UserId))
            .ToListAsync(ct);
        foreach (var link in links)
            link.VerifyIntegrity();
        return links.Where(link => link.IsSyncOn).Select(link => link.UserId).ToHashSet();
    }

    private static void VerifyUserDeviceLinks(IEnumerable<UserDevice> links)
    {
        foreach (var link in links)
            link.VerifyIntegrity();
    }

    private static string NormalizeFingerprint(string fingerprint) =>
        fingerprint.Replace(":", string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();
}
