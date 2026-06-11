using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PasswordManagerLocalBackend.Abstractions;
using PasswordManagerLocalBackend.Abstractions.Persistence;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Security;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Constants;
using PasswordManagerLocalBackend.Persistence;
using PasswordManagerLocalBackend.Repositories;
using PasswordManagerLocalBackend.Security;
using PasswordManagerLocalBackend.Services;
using PasswordManagerLocalBackend.Services.Hosted;
using PasswordManagerLocalBackend.Services.Tcp;
using SQLitePCL;

namespace PasswordManagerLocalBackend
{
    public static class BackendHost
    {
        private static IHost? _host;
        private static Task? _initTask;
        private static readonly object _lock = new();

        public static IServiceProvider Services
            => _host?.Services ?? throw new InvalidOperationException("BackendHost is not initialized.");

        public static bool IsInitialized
        {
            get
            {
                lock (_lock)
                {
                    return _host is not null;
                }
            }
        }

        public static Task StartInitializationAsync(IKeyProtector? platformKeyProtector = null)
        {
            lock (_lock)
            {
                if (_host is not null)
                    return Task.CompletedTask;

                if (_initTask is not null)
                    return _initTask;

                Batteries_V2.Init();
                _initTask = InitializeInternal(platformKeyProtector);
                return _initTask;
            }
        }

        public static async Task InitializeAsync(IKeyProtector? platformKeyProtector = null)
        {
            await StartInitializationAsync(platformKeyProtector);
        }

        public static async Task WaitUntilInitializedAsync(CancellationToken ct = default)
        {
            var initTask = StartInitializationAsync();
            await initTask.WaitAsync(ct);
        }

        private static async Task InitializeInternal(IKeyProtector? platformKeyProtector)
        {
            var host = Host.CreateDefaultBuilder(Array.Empty<string>())
                .ConfigureServices((context, services) =>
                {
                    var dbFolder = PathConstants.AppRootFolder;
                    var dbPath = Path.Combine(dbFolder, PathConstants.DbFileName);

                    if (platformKeyProtector is not null)
                    {
                        services.AddSingleton<IKeyProtector>(platformKeyProtector);
                    }
                    else
                    {
                        services.AddSingleton<IKeyProtector>(sp =>
                        {
                            var cfg = sp.GetRequiredService<IConfiguration>();
                            var passphrase =
                                cfg["Db:MasterPassphrase"] ??
                                Environment.GetEnvironmentVariable("APP_DB_MASTER_PASSPHRASE") ??
                                throw new InvalidOperationException(
                                    "Provide master passphrase via config (Db:MasterPassphrase) or env (APP_DB_MASTER_PASSPHRASE).");

                            return new PassphraseKeyProtector(System.Text.Encoding.UTF8.GetBytes(passphrase));
                        });
                    }

                    services.AddDbContext<AppDbContext>((sp, opts) =>
                    {
                        var protector = sp.GetRequiredService<IKeyProtector>();
                        var dbPassword = DbKeyManager.GetOrCreateSqlCipherPassword(protector);

                        var connStr = new SqliteConnectionStringBuilder
                        {
                            DataSource = dbPath,
                            Password = dbPassword
                        }.ToString();

                        opts.UseSqlite(connStr);
                    });

                    services.AddScoped<Endpoints>();
                    services.AddSingleton<IEndpoints, ScopedEndpoints>();

                    services.AddScoped<IUnitOfWork, AppUnitOfWork>();

                    services.AddScoped<IUserRepository, UserRepository>();
                    services.AddScoped<IDeviceRepository, DeviceRepository>();
                    services.AddScoped<IUserDeviceRepository, UserDeviceRepository>();
                    services.AddScoped<IGroupRepository, GroupRepository>();
                    services.AddScoped<ISyncQueueRepository, SyncQueueRepository>();
                    services.AddScoped<ISyncItemRepository, SyncItemRepository>();
                    services.AddScoped<ISyncTombstoneRepository, SyncTombstoneRepository>();
                    services.AddScoped<IDeviceIdentityRepository, DeviceIdentityRepository>();

                    services.AddScoped<IUserPasswordsService, UserPasswordsService>();
                    services.AddScoped<IPasswordService, PasswordService>();
                    services.AddScoped<IGroupService, GroupService>();
                    services.AddScoped<IGroupPasswordsService, GroupPasswordsService>();
                    services.AddScoped<IAuthService, AuthService>();
                    services.AddScoped<IUserService, UserService>();
                    services.AddScoped<IRememberMeService, RememberMeService>();
                    services.AddScoped<IUserProfileService, UserProfileService>();
                    services.AddScoped<IDeviceService, DeviceService>();
                    services.AddScoped<IDeviceSecurityService, DeviceSecurityService>();

                    services.AddSingleton<IKeyVaultService, KeyVaultService>();
                    services.AddMemoryCache();
                    services.AddSingleton<SafeMemoryCache>();
                    services.AddSingleton<IDataCachingService, DataCachingService>();
                    services.AddSingleton<ITokenService, TokenService>();

                    services.AddSingleton<IDeviceIdentityService, DeviceIdentityService>();
                    services.AddSingleton<ISyncTransportClientService, TcpSyncClientService>();
                    services.AddSingleton<ISyncDeviceIdentityService, SyncDeviceIdentityService>();
                    services.AddSingleton<IDiscoveredDeviceEndpointCache, DiscoveredDeviceEndpointCache>();
                    services.AddSingleton<IDeviceSyncTaskService, DeviceSyncTaskService>();
                    services.AddSingleton<ISyncRuntimeService, SyncRuntimeService>();
                    services.AddSingleton<IDeviceEnrollmentService, DeviceEnrollmentService>();
                    services.AddScoped<IOutgoingDeltaBuilderService, OutgoingDeltaBuilderService>();
                    services.AddScoped<INetworkDeltaService, NetworkDeltaService>();
                    services.AddScoped<IIncomingDeltaApplierService, IncomingDeltaApplierService>();
                    services.AddScoped<ISyncQueueService, SyncQueueService>();
                    services.AddScoped<ISyncService, SyncService>();

                    services.AddSingleton<MdnsPublisherHostedService>();
                    services.AddSingleton<MdnsBrowserHostedService>();

                    services.AddHostedService<ExpiredEntriesPurgeHostedService>();
                    services.AddHostedService<LocalDeviceCleanupHostedService>();
                    services.AddHostedService<SyncDeviceIdentityWarmupHostedService>();
                    services.AddSingleton<SyncPeerProtocolHandler>();
                    services.AddHostedService<TcpSyncServerHostedService>();
                    services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<MdnsPublisherHostedService>());
                    services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<MdnsBrowserHostedService>());
                    services.AddHostedService<SyncNetworkRefreshHostedService>();
                })
                .Build();

            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await AppDatabaseInitializer.InitializeAsync(db);
            }

            var deviceKeyStore = host.Services.GetRequiredService<IDeviceIdentityService>();
            await deviceKeyStore.InitializeAsync();

            await host.StartAsync();

            lock (_lock)
            {
                _host = host;
            }
        }
    }
}