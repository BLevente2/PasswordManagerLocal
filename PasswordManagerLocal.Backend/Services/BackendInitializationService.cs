using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Persistence;

namespace PasswordManagerLocal.Backend.Services;

public sealed class BackendInitializationService : IBackendInitializationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDeviceIdentityService _deviceIdentity;

    public BackendInitializationService(
        IServiceScopeFactory scopeFactory,
        IDeviceIdentityService deviceIdentity)
    {
        _scopeFactory = scopeFactory;
        _deviceIdentity = deviceIdentity;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await AppDatabaseInitializer.InitializeAsync(db, cancellationToken);
        }

        await _deviceIdentity.InitializeAsync(cancellationToken);

        bool shouldEnableSync;
        using (var scope = _scopeFactory.CreateScope())
        {
            var localUserDevices = scope.ServiceProvider.GetRequiredService<ILocalUserDeviceRepository>();
            shouldEnableSync = await localUserDevices.AnySyncOnAsync(cancellationToken);
        }

        if (_deviceIdentity.IsSyncOn != shouldEnableSync)
            await _deviceIdentity.SetSyncOnAsync(shouldEnableSync, cancellationToken);
    }
}
