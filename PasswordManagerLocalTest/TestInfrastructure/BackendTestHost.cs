using Microsoft.Extensions.Caching.Memory;
using PasswordManagerLocalBackend;
using PasswordManagerLocalBackend.Abstractions;
using PasswordManagerLocalBackend.Abstractions.Persistence;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Security;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Security;
using PasswordManagerLocalBackend.Services;
using PasswordManagerLocalTest.Fakes;
using System.Text;

namespace PasswordManagerLocalTest.TestInfrastructure;

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
        sc.AddSingleton<IUserDeviceRepository, FakeUserDeviceRepository>();
        sc.AddSingleton<IDeviceRepository, FakeDeviceRepository>();
        sc.AddSingleton<ISyncQueueRepository, FakeSyncQueueRepository>();
        sc.AddSingleton<ILocalUserDeviceRepository, FakeLocalUserDeviceRepository>();
        sc.AddSingleton<IDeviceIdentityService, FakeDeviceIdentityService>();
        sc.AddSingleton<ISyncQueueService, FakeSyncQueueService>();
        sc.AddSingleton<ISyncRuntimeService, FakeSyncRuntimeService>();
        sc.AddSingleton<ISyncDeviceIdentityService, FakeSyncDeviceIdentityService>();
        sc.AddSingleton<IUnitOfWork, FakeUnitOfWork>();

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