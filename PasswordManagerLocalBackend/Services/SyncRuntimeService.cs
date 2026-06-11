using Microsoft.Extensions.Hosting;
using PasswordManagerLocalBackend.Abstractions.Services;

namespace PasswordManagerLocalBackend.Services;

public sealed class SyncRuntimeService : ISyncRuntimeService
{
    private readonly IDeviceIdentityService _identity;
    private readonly ISyncDeviceIdentityService _syncDeviceIdentities;
    private readonly IDiscoveredDeviceEndpointCache _endpointCache;
    private readonly IDeviceSyncTaskService _deviceSyncTasks;
    private readonly IEnumerable<IHostedService> _hostedServices;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SyncRuntimeService(
        IDeviceIdentityService identity,
        ISyncDeviceIdentityService syncDeviceIdentities,
        IDiscoveredDeviceEndpointCache endpointCache,
        IDeviceSyncTaskService deviceSyncTasks,
        IEnumerable<IHostedService> hostedServices)
    {
        _identity = identity;
        _syncDeviceIdentities = syncDeviceIdentities;
        _endpointCache = endpointCache;
        _deviceSyncTasks = deviceSyncTasks;
        _hostedServices = hostedServices;
    }




    public async Task SetSyncEnabledAsync(bool isSyncOn, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_identity.IsSyncOn == isSyncOn)
            {
                if (isSyncOn)
                    await StartCoreAsync(ct);
                else
                    await StopCoreAsync(ct);

                return;
            }

            await _identity.SetSyncOnAsync(isSyncOn, ct);

            if (isSyncOn)
                await StartCoreAsync(ct);
            else
                await StopCoreAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }


    public async Task StartAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await StartCoreAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }


    public async Task StopAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await StopCoreAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }


    private async Task StartCoreAsync(CancellationToken ct)
    {
        if (!_identity.IsSyncOn)
            return;

        foreach (var hostedService in ListControlledServices().OrderBy(s => s.StartOrder))
            await hostedService.StartAsync(ct);
    }


    private async Task StopCoreAsync(CancellationToken ct)
    {
        await _deviceSyncTasks.StopAllAsync(ct);

        foreach (var hostedService in ListControlledServices().OrderByDescending(s => s.StartOrder))
            await hostedService.StopAsync(ct);

        _syncDeviceIdentities.Clear();
        _endpointCache.Clear();
    }


    private IReadOnlyList<ISyncControlledHostedService> ListControlledServices() =>
        _hostedServices
            .OfType<ISyncControlledHostedService>()
            .ToList();
}
