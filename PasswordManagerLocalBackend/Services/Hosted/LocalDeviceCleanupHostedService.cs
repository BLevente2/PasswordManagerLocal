using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PasswordManagerLocalBackend.Abstractions.Persistence;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;

namespace PasswordManagerLocalBackend.Services.Hosted;

public sealed class LocalDeviceCleanupHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDeviceIdentityService _identity;
    private readonly ISyncDeviceIdentityService _syncDeviceIdentities;

    public LocalDeviceCleanupHostedService(
        IServiceScopeFactory scopeFactory,
        IDeviceIdentityService identity,
        ISyncDeviceIdentityService syncDeviceIdentities)
    {
        _scopeFactory = scopeFactory;
        _identity = identity;
        _syncDeviceIdentities = syncDeviceIdentities;
    }




    public async Task StartAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var devices = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var localDevices = await devices.ListLocalSelfDevicesAsync(
            _identity.LocalDeviceId,
            _identity.SignPublicKey,
            _identity.FingerprintHex,
            ct);

        if (localDevices.Count == 0)
            return;

        foreach (var device in localDevices)
        {
            _syncDeviceIdentities.TryRemove(device);
            devices.Delete(device);
        }

        await uow.SaveChangesAsync(ct);
    }


    public Task StopAsync(CancellationToken ct = default) =>
        Task.CompletedTask;
}
