using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Abstractions.Caching;
using PasswordManagerLocal.Backend.Abstractions.State;

namespace PasswordManagerLocal.Backend.Services;

public sealed class SyncRuntimeService : ISyncRuntimeService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDeviceIdentityService _identity;
    private readonly IEnrollmentRuntimeState _enrollmentState;
    private readonly ISyncDeviceIdentityService _syncDeviceIdentities;
    private readonly IDiscoveredDeviceEndpointCache _endpointCache;
    private readonly IDeviceSyncTaskService _deviceSyncTasks;
    private readonly IEnumerable<ISyncControlledHostedService> _controlledServices;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SyncRuntimeService(
        IServiceScopeFactory scopeFactory,
        IDeviceIdentityService identity,
        IEnrollmentRuntimeState enrollmentState,
        ISyncDeviceIdentityService syncDeviceIdentities,
        IDiscoveredDeviceEndpointCache endpointCache,
        IDeviceSyncTaskService deviceSyncTasks,
        IEnumerable<ISyncControlledHostedService> controlledServices)
    {
        _scopeFactory = scopeFactory;
        _identity = identity;
        _enrollmentState = enrollmentState;
        _syncDeviceIdentities = syncDeviceIdentities;
        _endpointCache = endpointCache;
        _deviceSyncTasks = deviceSyncTasks;
        _controlledServices = controlledServices;
    }

    public async Task RefreshSyncEnabledAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var localUsers = scope.ServiceProvider.GetRequiredService<ILocalUserDeviceRepository>();
        var shouldEnable = await localUsers.AnySyncOnAsync(ct);

        await _lock.WaitAsync(ct);
        try
        {
            if (_identity.IsSyncOn != shouldEnable)
                await _identity.SetSyncOnAsync(shouldEnable, ct);

            if (shouldEnable || _enrollmentState.IsActive)
                await StartCoreAsync(ct);
            else
                await StopCoreAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task BeginEnrollmentOnlyAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _enrollmentState.Activate();
            await StartCoreAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task EndEnrollmentOnlyAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _enrollmentState.Deactivate();
            if (_identity.IsSyncOn)
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
            _enrollmentState.Deactivate();
            await StopCoreAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task StartCoreAsync(CancellationToken ct)
    {
        if (!_identity.IsSyncOn && !_enrollmentState.IsActive)
            return;

        try
        {
            foreach (var hostedService in ListControlledServices().OrderBy(s => s.StartOrder))
                await hostedService.StartAsync(ct);
        }
        catch (Exception startException)
        {
            try
            {
                await StopCoreAsync(CancellationToken.None);
            }
            catch (Exception stopException)
            {
                throw new AggregateException(startException, stopException);
            }

            throw;
        }
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
        _controlledServices.ToList();
}
