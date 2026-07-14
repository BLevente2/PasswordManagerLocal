using Microsoft.Extensions.Caching.Memory;
using PasswordManagerLocal.Backend;
using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Backend.Abstractions.Caching;
using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Security;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Requests;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Caching;
using PasswordManagerLocal.Test.Fakes;
using System.Text;

namespace PasswordManagerLocal.Test.TestInfrastructure;

public sealed class BackendTestHost : IDisposable
{
    private readonly ServiceProvider _sp;

    public BackendTestHost()
    {
        var sc = new ServiceCollection();

        sc.AddSingleton<IMemoryCache>(_ => new MemoryCache(new MemoryCacheOptions { SizeLimit = 100_000 }));
        sc.AddSingleton<SafeMemoryCache>();

        sc.AddSingleton<ITokenService, TokenService>();
        sc.AddSingleton<IKeyVaultService, KeyVaultService>();

        sc.AddSingleton<IDataCachingService>(sp =>
        {
            var cache = sp.GetRequiredService<SafeMemoryCache>();
            var tokens = sp.GetRequiredService<ITokenService>();
            return new DataCachingService(cache, tokens);
        });

        sc.AddSingleton<IKeyProtector, TestKeyProtector>();
        sc.AddSingleton<IEndpoints, Endpoints>();

        sc.AddSingleton<IUserRepository, InMemoryUserRepository>();
        sc.AddSingleton<IGroupRepository, FakeGroupRepository>();
        sc.AddSingleton<FakeUserDeviceRepository>();
        sc.AddSingleton<IUserDeviceRepository>(sp => sp.GetRequiredService<FakeUserDeviceRepository>());
        sc.AddSingleton<IDeviceRepository, FakeDeviceRepository>();
        sc.AddSingleton<ISyncQueueRepository, FakeSyncQueueRepository>();
        sc.AddSingleton<FakeLocalUserDeviceRepository>();
        sc.AddSingleton<ILocalUserDeviceRepository>(sp => sp.GetRequiredService<FakeLocalUserDeviceRepository>());
        sc.AddSingleton<ISyncRouteRepository, FakeSyncRouteRepository>();
        sc.AddSingleton<IDeviceIdentityService, FakeDeviceIdentityService>();
        sc.AddSingleton<FakeSyncQueueService>();
        sc.AddSingleton<ISyncQueueService>(sp => sp.GetRequiredService<FakeSyncQueueService>());
        sc.AddSingleton<ISyncChangeQueueService>(sp => sp.GetRequiredService<FakeSyncQueueService>());
        sc.AddSingleton<IUserSyncCatchUpService>(sp => sp.GetRequiredService<FakeSyncQueueService>());
        sc.AddSingleton<IPendingSyncActivationService>(sp => sp.GetRequiredService<FakeSyncQueueService>());
        sc.AddSingleton<ISyncRuntimeService, FakeSyncRuntimeService>();
        sc.AddSingleton<ISyncDeviceIdentityService, FakeSyncDeviceIdentityService>();
        sc.AddSingleton<IDiscoveredDeviceEndpointCache, DiscoveredDeviceEndpointCache>();
        sc.AddSingleton<IUnitOfWork, FakeUnitOfWork>();

        sc.AddSingleton<IUserDataBundleIntegrityService, UserDataBundleIntegrityService>();
        sc.AddSingleton<IUserSessionService, UserSessionService>();
        sc.AddSingleton<IUserLookupService, UserLookupService>();
        sc.AddSingleton<IUserDataReaderService, UserDataReaderService>();
        sc.AddSingleton<IUserDataPersistenceValidator, UserDataPersistenceValidator>();
        sc.AddSingleton<IUserDataWriterService, UserDataWriterService>();
        sc.AddSingleton<IUserDeletionService, UserDeletionService>();
        sc.AddSingleton<IUserService, UserService>();
        sc.AddSingleton<IUserProfileService, UserProfileService>();
        sc.AddSingleton<IRememberMeService, RememberMeService>();
        sc.AddSingleton<IAuthService, AuthService>();
        sc.AddSingleton<IPasswordService, PasswordService>();
        sc.AddSingleton<ICustomUserColorService, CustomUserColorService>();
        sc.AddSingleton<IPasswordTagService, PasswordTagService>();
        sc.AddSingleton<IUserPasswordsService, UserPasswordsService>();
        sc.AddSingleton<IUserCustomColorService, UserCustomColorService>();
        sc.AddSingleton<IUserPasswordTagService, UserPasswordTagService>();
        sc.AddSingleton<IDeviceService, DeviceService>();

        _sp = sc.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    public IServiceProvider Services => _sp;

    public RegistrationRequest CreateValidRegistrationRequest(string username = "testuser") =>
        new RegistrationRequest
        {
            Username = username,
            Password = Encoding.UTF8.GetBytes("P@ssw0rd12345678"),
            FirstName = "Test",
            LastName = "User",
            Email = "test@example.com",
            RememberMe = false
        };


    public LoginRequest CreateValidLoginRequest(string username = "testuser", bool rememberMe = false) =>
        new LoginRequest
        {
            Username = username,
            Password = Encoding.UTF8.GetBytes("P@ssw0rd12345678"),
            RememberMe = rememberMe
        };


    public void Dispose() =>
        _sp.Dispose();
}