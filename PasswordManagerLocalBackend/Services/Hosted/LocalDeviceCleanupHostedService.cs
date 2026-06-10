using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PasswordManagerLocalBackend.Abstractions.Persistence;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Services.Hosted;

public sealed class LocalDeviceCleanupHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDeviceIdentityService _identity;

    public LocalDeviceCleanupHostedService(
        IServiceScopeFactory scopeFactory,
        IDeviceIdentityService identity)
    {
        _scopeFactory = scopeFactory;
        _identity = identity;
    }




    public async Task StartAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var devices = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var localSelfDevices = await devices.ListLocalSelfDevicesAsync(_identity.LocalDeviceId, _identity.SignPublicKey, _identity.FingerprintHex, ct);
        var duplicateLocalDevices = localSelfDevices
            .Where(d => d.Id != _identity.LocalDeviceId)
            .ToList();

        if (duplicateLocalDevices.Count != 0)
        {
            foreach (var duplicateDevice in duplicateLocalDevices)
                devices.Delete(duplicateDevice);

            await uow.SaveChangesAsync(ct);
        }

        var device = localSelfDevices.FirstOrDefault(d => d.Id == _identity.LocalDeviceId)
            ?? await devices.GetByIdWithUserDevicesAsync(_identity.LocalDeviceId, ct);
        var now = DateTimeOffset.UtcNow;

        if (device is null)
        {
            device = new Device
            {
                Id = _identity.LocalDeviceId,
                PublicKey = _identity.AgreementPublicKey,
                SignPublicKey = _identity.SignPublicKey,
                TlsCertFingerprint = _identity.FingerprintHex,
                DeviceName = _identity.DeviceName,
                LastSync = now.UtcDateTime,
                LastSeen = now.UtcDateTime,
                IsTrusted = true,
                IsBlocked = false,
                LastModifiedAt = now
            };

            device.GenerateIntegrityHash();
            await devices.AddAsync(device, ct);
            await uow.SaveChangesAsync(ct);
            return;
        }

        device.PublicKey = _identity.AgreementPublicKey;
        device.SignPublicKey = _identity.SignPublicKey;
        device.TlsCertFingerprint = _identity.FingerprintHex;
        device.DeviceName = _identity.DeviceName;
        device.IsTrusted = true;
        device.IsBlocked = false;
        device.BlockedReason = null;
        device.BlockedAt = null;
        device.LastModifiedAt = now;
        device.GenerateIntegrityHash();
        devices.Update(device);
        await uow.SaveChangesAsync(ct);
    }


    public Task StopAsync(CancellationToken ct = default) =>
        Task.CompletedTask;
}
