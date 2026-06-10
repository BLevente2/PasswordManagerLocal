using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;

namespace PasswordManagerLocalBackend.Services.Hosted;

public sealed class SyncDeviceIdentityWarmupHostedService : ISyncControlledHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISyncDeviceIdentityService _syncDeviceIdentities;
    private readonly IDeviceIdentityService _identity;

    public SyncDeviceIdentityWarmupHostedService(
        IServiceScopeFactory scopeFactory,
        ISyncDeviceIdentityService syncDeviceIdentities,
        IDeviceIdentityService identity)
    {
        _scopeFactory = scopeFactory;
        _syncDeviceIdentities = syncDeviceIdentities;
        _identity = identity;
    }




    public int StartOrder => 10;




    public async Task StartAsync(CancellationToken ct = default)
    {
        _syncDeviceIdentities.Clear();

        if (!_identity.IsSyncOn)
            return;

        using var scope = _scopeFactory.CreateScope();
        var devices = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
        var pendingDevices = await devices.ListDevicesNeedingSyncAsync(ct);
        _syncDeviceIdentities.TryAdd(pendingDevices);
    }


    public Task StopAsync(CancellationToken ct = default)
    {
        _syncDeviceIdentities.Clear();
        return Task.CompletedTask;
    }
}
