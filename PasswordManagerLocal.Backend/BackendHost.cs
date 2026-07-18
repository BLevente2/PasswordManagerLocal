using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Security;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Constants;
using PasswordManagerLocal.Backend.Hosting;
using PasswordManagerLocal.Backend.Persistence;
using PasswordManagerLocal.Backend.Repositories;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Services.Hosted;
using PasswordManagerLocal.Backend.Services.Discovery;
using PasswordManagerLocal.Backend.Utils;
using SQLitePCL;
using System.Text;
using PasswordManagerLocal.Backend.Abstractions.Caching;
using PasswordManagerLocal.Backend.Abstractions.Providers;
using PasswordManagerLocal.Backend.Abstractions.State;
using PasswordManagerLocal.Backend.Caching;
using PasswordManagerLocal.Backend.Providers;
using PasswordManagerLocal.Backend.State;
using PasswordManagerLocal.Backend.Sync.Tcp;

namespace PasswordManagerLocal.Backend
{
    public static class BackendHost
    {
        private static BackendServiceHost? _host;
        private static Task? _initTask;
        private static readonly object _lock = new();
        private static IKeyProtector? _platformKeyProtector;

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

        public static void ConfigurePlatformKeyProtector(IKeyProtector platformKeyProtector)
        {
            ArgumentNullException.ThrowIfNull(platformKeyProtector);

            lock (_lock)
            {
                if (_host is not null || _initTask is not null)
                    return;

                _platformKeyProtector = platformKeyProtector;
            }
        }

        public static Task StartInitializationAsync(IKeyProtector? platformKeyProtector = null)
        {
            lock (_lock)
            {
                if (_host is not null)
                    return Task.CompletedTask;

                if (platformKeyProtector is not null)
                    _platformKeyProtector = platformKeyProtector;

                if (_initTask is not null)
                    return _initTask;

                var keyProtector = _platformKeyProtector;
                _initTask = Task.Run(
                    () => InitializeInternal(keyProtector),
                    CancellationToken.None);
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

        public static async Task ResetDatabaseAndReinitializeAsync(CancellationToken ct = default)
        {
            IKeyProtector? platformKeyProtector;

            lock (_lock)
            {
                if (_host is not null)
                    throw new InvalidOperationException("The database can only be reset after backend startup has failed.");

                platformKeyProtector = _platformKeyProtector;
                _initTask = null;
            }

            DeleteDatabaseFiles();

            try
            {
                await StartInitializationAsync(platformKeyProtector).WaitAsync(ct);
            }
            catch
            {
                lock (_lock)
                {
                    if (_host is null)
                        _initTask = null;
                }

                DeleteDatabaseFiles();
                throw;
            }
        }

        private static async Task InitializeInternal(IKeyProtector? platformKeyProtector)
        {
            Batteries_V2.Init();
            DeviceEnrollmentTrace.InitializeForCurrentBuild();
            BackendServiceHost? host = null;

            try
            {
                var services = new ServiceCollection();
                ConfigureServices(services, platformKeyProtector);
                host = new BackendServiceHost(services.BuildServiceProvider());

                using (var scope = host.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    await AppDatabaseInitializer.InitializeAsync(db);
                }

                var deviceKeyStore = host.Services.GetRequiredService<IDeviceIdentityService>();
                await deviceKeyStore.InitializeAsync();

                bool shouldEnableSync;
                using (var scope = host.Services.CreateScope())
                {
                    var localUserDevices = scope.ServiceProvider.GetRequiredService<ILocalUserDeviceRepository>();
                    shouldEnableSync = await localUserDevices.AnySyncOnAsync();
                }

                if (deviceKeyStore.IsSyncOn != shouldEnableSync)
                    await deviceKeyStore.SetSyncOnAsync(shouldEnableSync);

                await host.StartAsync();
                await host.Services.GetRequiredService<ISyncRuntimeService>().RefreshSyncEnabledAsync();

                lock (_lock)
                {
                    _host = host;
                }
            }
            catch
            {
                if (host is not null)
                {
                    try
                    {
                        await host.DisposeAsync();
                    }
                    catch
                    {
                    }
                }

                throw;
            }
        }

        private static void ConfigureServices(IServiceCollection services, IKeyProtector? platformKeyProtector)
        {
            var dbFolder = PathConstants.AppRootFolder;
            var dbPath = Path.Combine(dbFolder, PathConstants.DbFileName);

            if (platformKeyProtector is not null)
            {
                services.AddSingleton<IKeyProtector>(platformKeyProtector);
            }
            else
            {
                services.AddSingleton<IKeyProtector>(_ =>
                {
                    var passphrase =
                        Environment.GetEnvironmentVariable("APP_DB_MASTER_PASSPHRASE") ??
                        Environment.GetEnvironmentVariable("Db__MasterPassphrase") ??
                        throw new InvalidOperationException(
                            "Provide the master passphrase via APP_DB_MASTER_PASSPHRASE or Db__MasterPassphrase.");

                    return new PassphraseKeyProtector(Encoding.UTF8.GetBytes(passphrase));
                });
            }

            services.AddSingleton<RelationshipIntegrityMaterializationInterceptor>();

            services.AddDbContextPool<AppDbContext>((sp, opts) =>
            {
                var protector = sp.GetRequiredService<IKeyProtector>();
                var dbPassword = DbConfigManager.GetOrCreateSqlCipherPassword(protector);

                var connStr = new SqliteConnectionStringBuilder
                {
                    DataSource = dbPath,
                    Password = dbPassword
                }.ToString();

                opts.UseSqlite(connStr);
                opts.AddInterceptors(sp.GetRequiredService<RelationshipIntegrityMaterializationInterceptor>());
            });

            services.AddSingleton<IEndpoints, Endpoints>();

            services.AddScoped<IUnitOfWork, AppUnitOfWork>();

            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IDeviceRepository, DeviceRepository>();
            services.AddScoped<IUserDeviceRepository, UserDeviceRepository>();
            services.AddScoped<ILocalUserDeviceRepository, LocalUserDeviceRepository>();
            services.AddScoped<ISyncRouteRepository, SyncRouteRepository>();
            services.AddScoped<IGroupRepository, GroupRepository>();
            services.AddScoped<ISyncQueueRepository, SyncQueueRepository>();
            services.AddScoped<ISyncItemRepository, SyncItemRepository>();
            services.AddScoped<ISyncTombstoneRepository, SyncTombstoneRepository>();
            services.AddScoped<IDeviceIdentityRepository, DeviceIdentityRepository>();
            services.AddScoped<IUserSyncSnapshotRepository, UserSyncSnapshotRepository>();
            services.AddScoped<IUserSyncStateRepository, UserSyncStateRepository>();
            services.AddScoped<IUserRevisionKnowledgeRepository, UserRevisionKnowledgeRepository>();

            services.AddScoped<IUserPasswordsService, UserPasswordsService>();
            services.AddScoped<IUserCustomColorService, UserCustomColorService>();
            services.AddScoped<IUserPasswordTagService, UserPasswordTagService>();
            services.AddScoped<IPasswordService, PasswordService>();
            services.AddScoped<ICustomUserColorService, CustomUserColorService>();
            services.AddScoped<IPasswordTagService, PasswordTagService>();
            services.AddScoped<IGroupService, GroupService>();
            services.AddScoped<IGroupPasswordsService, GroupPasswordsService>();
            services.AddScoped<IUserDataBundleIntegrityService, UserDataBundleIntegrityService>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IUserSessionService, UserSessionService>();
            services.AddScoped<IUserLookupService, UserLookupService>();
            services.AddScoped<IUserDataReaderService, UserDataReaderService>();
            services.AddScoped<IUserDataPersistenceValidator, UserDataPersistenceValidator>();
            services.AddScoped<IUserDataWriterService, UserDataWriterService>();
            services.AddScoped<IUserDeletionService, UserDeletionService>();
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

            services.AddSingleton<ILocalDeviceTypeProvider, LocalDeviceTypeProvider>();
            services.AddSingleton<IDeviceIdentityService, DeviceIdentityService>();
            services.AddSingleton<ISyncTransportClientService, TcpSyncClientService>();
            services.AddSingleton<ISyncDeviceIdentityService, SyncDeviceIdentityService>();
            services.AddSingleton<IDiscoveredDeviceEndpointCache, DiscoveredDeviceEndpointCache>();
            services.AddSingleton<IDeviceSyncTaskService, DeviceSyncTaskService>();
            services.AddSingleton<IEnrollmentRuntimeState, EnrollmentRuntimeState>();
            services.AddSingleton<ILocalNetworkAddressService, LocalNetworkAddressService>();
            services.AddSingleton<ILocalDiscoveryTransport, UdpLocalDiscoveryTransport>();
            services.AddSingleton<LocalDiscoveryHostedService>();
            services.AddSingleton<ILocalDiscoveryService>(sp => sp.GetRequiredService<LocalDiscoveryHostedService>());
            services.AddSingleton<ISyncRuntimeService, SyncRuntimeService>();
            services.AddSingleton<IDeviceEnrollmentEndpointService, DeviceEnrollmentEndpointService>();
            services.AddSingleton<IDeviceEnrollmentLocalLinkService, DeviceEnrollmentLocalLinkService>();
            services.AddSingleton<IDeviceEnrollmentRegistrationService, DeviceEnrollmentRegistrationService>();
            services.AddSingleton<IDeviceEnrollmentSnapshotService, DeviceEnrollmentSnapshotService>();
            services.AddSingleton<IDeviceEnrollmentSnapshotTransferService, DeviceEnrollmentSnapshotTransferService>();
            services.AddSingleton<IDeviceEnrollmentSnapshotImporterService, DeviceEnrollmentSnapshotImporterService>();
            services.AddSingleton<IDeviceEnrollmentService, DeviceEnrollmentService>();

            services.AddScoped<IUserPasswordsDataMergeService, UserPasswordsDataMergeService>();
            services.AddScoped<IUserDevicesDataMergeService, UserDevicesDataMergeService>();
            services.AddScoped<ISyncRelationshipReconciliationService, SyncRelationshipReconciliationService>();
            services.AddScoped<IUserDataBundleSyncService, UserDataBundleSyncService>();
            services.AddScoped<IUserSnapshotPublisherService, UserSnapshotPublisherService>();
            services.AddScoped<IUserSnapshotInboxService, UserSnapshotInboxService>();
            services.AddScoped<IUserSnapshotMergeCoordinator, UserSnapshotMergeCoordinator>();
            services.AddScoped<IUserSnapshotAntiEntropyService, UserSnapshotAntiEntropyService>();
            services.AddScoped<IUserSyncKeyResolverService, UserSyncKeyResolverService>();
            services.AddScoped<IUserDeltaApplierService, UserDeltaApplierService>();
            services.AddScoped<INetworkDeltaProtocolService, NetworkDeltaProtocolService>();
            services.AddScoped<INetworkDeltaReplayService, NetworkDeltaReplayService>();
            services.AddScoped<INetworkDeltaPayloadApplierService, NetworkDeltaPayloadApplierService>();
            services.AddScoped<INetworkDeltaLifecycleService, NetworkDeltaLifecycleService>();
            services.AddScoped<IOutgoingDeltaBuilderService, OutgoingDeltaBuilderService>();
            services.AddScoped<INetworkDeltaService, NetworkDeltaService>();
            services.AddScoped<IIncomingDeltaApplierService, IncomingDeltaApplierService>();
            services.AddScoped<ISyncAuthorizationService, SyncAuthorizationService>();
            services.AddScoped<ILocalDeviceMatcherService, LocalDeviceMatcherService>();
            services.AddScoped<ISyncItemLifecycleService, SyncItemLifecycleService>();
            services.AddScoped<ISyncTargetResolverService, SyncTargetResolverService>();
            services.AddScoped<IPendingSyncActivationService, PendingSyncActivationService>();
            services.AddScoped<ISyncQueueWriterService, SyncQueueWriterService>();
            services.AddScoped<ISyncChangeQueueService, SyncChangeQueueService>();
            services.AddScoped<IUserSyncCatchUpService, UserSyncCatchUpService>();
            services.AddScoped<ISyncQueueService, SyncQueueService>();
            services.AddScoped<ISyncService, SyncService>();

            services.AddSingleton<ExpiredEntriesPurgeHostedService>();
            services.AddSingleton<IBackendHostedService>(sp => sp.GetRequiredService<ExpiredEntriesPurgeHostedService>());
            services.AddSingleton<LocalDeviceCleanupHostedService>();
            services.AddSingleton<IBackendHostedService>(sp => sp.GetRequiredService<LocalDeviceCleanupHostedService>());

            services.AddSingleton<SyncPeerProtocolHandler>();
            services.AddSingleton<SyncDeviceIdentityWarmupHostedService>();
            services.AddSingleton<TcpSyncServerHostedService>();
            services.AddSingleton<SyncNetworkRefreshHostedService>();

            services.AddSingleton<ISyncControlledHostedService>(sp => sp.GetRequiredService<SyncDeviceIdentityWarmupHostedService>());
            services.AddSingleton<ISyncControlledHostedService>(sp => sp.GetRequiredService<TcpSyncServerHostedService>());
            services.AddSingleton<ISyncControlledHostedService>(sp => sp.GetRequiredService<LocalDiscoveryHostedService>());
            services.AddSingleton<ISyncControlledHostedService>(sp => sp.GetRequiredService<SyncNetworkRefreshHostedService>());
        }

        private static void DeleteDatabaseFiles()
        {
            var root = PathConstants.AppRootFolder;
            var databasePath = Path.Combine(root, PathConstants.DbFileName);
            var configPath = Path.Combine(root, PathConstants.DbConfigFileName);
            var legacyKeyPath = Path.Combine(root, PathConstants.LegacyDbKeyFileName);

            DeleteFileIfExists(databasePath);
            DeleteFileIfExists($"{databasePath}-wal");
            DeleteFileIfExists($"{databasePath}-shm");
            DeleteFileIfExists($"{databasePath}-journal");
            DeleteFileIfExists(configPath);
            DeleteFileIfExists(legacyKeyPath);

            foreach (var temporaryConfigPath in Directory.EnumerateFiles(root, $"{PathConstants.DbConfigFileName}.*.tmp"))
                DeleteFileIfExists(temporaryConfigPath);
        }

        private static void DeleteFileIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
