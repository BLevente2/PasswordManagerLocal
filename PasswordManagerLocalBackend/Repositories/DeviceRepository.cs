using Microsoft.EntityFrameworkCore;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Persistence;

namespace PasswordManagerLocalBackend.Repositories;

public sealed class DeviceRepository : GenericRepositoryBase<Device>, IDeviceRepository
{
    public DeviceRepository(AppDbContext context) : base(context.Devices) { }

    public override Task<Device?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<IReadOnlyList<Device>> ListDevicesNeedingSyncAsync(CancellationToken ct = default)
    {
        var devices = await Set.AsNoTracking()
            .Where(d => d.IsTrusted &&
                        !d.IsBlocked &&
                        d.TlsCertFingerprint != string.Empty &&
                        d.ItemsNeedingSync.Any(q => q.ProcessedAt == null))
            .ToListAsync(ct);

        return devices.Where(d => d.PublicKey.Length != 0 && d.SignPublicKey.Length != 0).ToList();
    }

    public Task<Device?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken ct = default) =>
        Set.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<Device?> GetByIdWithUsersAsync(Guid id, CancellationToken ct = default) =>
        Set.Include(d => d.UserDevices)
            .ThenInclude(ud => ud.User)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<Device?> GetByIdAsNoTrackingWithUsersAsync(Guid id, CancellationToken ct = default) =>
        Set.AsNoTracking()
            .Include(d => d.UserDevices)
            .ThenInclude(ud => ud.User)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<Device?> GetByIdWithUserDevicesAsync(Guid id, CancellationToken ct = default) =>
        Set.Include(d => d.UserDevices).FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<Device?> GetBySignPublicKeyAsync(byte[] signPublicKey, CancellationToken ct = default)
    {
        if (signPublicKey.Length == 0)
            return null;

        var devices = await Set.ToListAsync(ct);
        return devices.FirstOrDefault(d => d.SignPublicKey.SequenceEqual(signPublicKey));
    }

    public Task<Device?> GetByTlsCertFingerprintAsync(string tlsCertFingerprint, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tlsCertFingerprint))
            return Task.FromResult<Device?>(null);

        var normalized = NormalizeFingerprint(tlsCertFingerprint);
        return Set.FirstOrDefaultAsync(d => d.TlsCertFingerprint == normalized, ct);
    }

    public Task<Device?> GetByTlsCertFingerprintWithUserDevicesAsync(string tlsCertFingerprint, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tlsCertFingerprint))
            return Task.FromResult<Device?>(null);

        var normalized = NormalizeFingerprint(tlsCertFingerprint);
        return Set
            .Include(d => d.UserDevices)
            .FirstOrDefaultAsync(d => d.TlsCertFingerprint == normalized, ct);
    }

    public async Task<IReadOnlyList<Device>> ListLocalSelfDevicesAsync(
        Guid localDeviceId,
        byte[] signPublicKey,
        string tlsCertFingerprint,
        CancellationToken ct = default)
    {
        var normalizedFingerprint = NormalizeFingerprint(tlsCertFingerprint);
        var devices = await Set.Include(d => d.UserDevices).ToListAsync(ct);
        return devices.Where(d =>
                d.Id == localDeviceId ||
                (signPublicKey.Length != 0 && d.SignPublicKey.SequenceEqual(signPublicKey)) ||
                (normalizedFingerprint.Length != 0 && d.TlsCertFingerprint == normalizedFingerprint))
            .ToList();
    }

    public async Task<IReadOnlyList<Device>> ListByIdsWithUserDevicesAsNoTrackingAsync(
        IReadOnlyCollection<Guid> deviceIds,
        Guid excludedDeviceId,
        CancellationToken ct = default)
    {
        if (deviceIds.Count == 0)
            return [];

        return await Set.AsNoTracking()
            .Include(d => d.UserDevices)
            .Where(d => d.Id != excludedDeviceId && deviceIds.Contains(d.Id))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Device>> ListTrustedUnblockedAsync(CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Where(d => d.IsTrusted && !d.IsBlocked)
            .ToListAsync(ct);

    private static string NormalizeFingerprint(string fingerprint) =>
        fingerprint.Replace(":", string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();
}
