using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Security;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Abstractions.State;
using PasswordManagerLocal.Backend.Abstractions.Sync.Discovery;
using PasswordManagerLocal.Backend.Configuration;
using PasswordManagerLocal.Backend.Internal.Devices;
using PasswordManagerLocal.Backend.Persistence;
using PasswordManagerLocal.Backend.Repositories;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Services.Discovery;
using PasswordManagerLocal.Backend.Services.Hosted;
using PasswordManagerLocal.Backend.State;
using PasswordManagerLocal.Backend.Sync.Discovery;
using PasswordManagerLocal.Backend.Sync.Tcp;
using PasswordManagerLocal.Backend.Utils;

namespace PasswordManagerLocal.Backend.DependencyInjection;

public static class BackendServiceCollectionExtensions
{
    public static IServiceCollection AddPasswordManagerLocalBackend(
        this IServiceCollection services,
        BackendStoragePaths storagePaths,
        IKeyProtector keyProtector,
        ILocalDiscoveryNetworkLease discoveryNetworkLease)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(storagePaths);
        ArgumentNullException.ThrowIfNull(keyProtector);
        ArgumentNullException.ThrowIfNull(discoveryNetworkLease);

        services.AddSingleton(storagePaths);
        services.AddSingleton<IKeyProtector>(keyProtector);
        services.AddSingleton<ILocalDiscoveryNetworkLease>(discoveryNetworkLease);
        services.AddSingleton<IBackendInitializationService, BackendInitializationService>();

        services.AddSingleton<RelationshipIntegrityMaterializationInterceptor>();

        services.AddDbContextPool<AppDbContext>((sp, opts) =>
        {
            var paths = sp.GetRequiredService<BackendStoragePaths>();
            var protector = sp.GetRequiredService<IKeyProtector>();
            var dbPassword = DbConfigManager.GetOrCreateSqlCipherPassword(paths, protector);

            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = paths.DatabasePath,
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
        services.AddScoped<IUserControlOperationRepository, UserControlOperationRepository>();
        services.AddScoped<IUserControlStateRepository, UserControlStateRepository>();
        services.AddScoped<IUserMembershipAuthorizationRepository, UserMembershipAuthorizationRepository>();
        services.AddScoped<IUserOriginRemovalCutoffRepository, UserOriginRemovalCutoffRepository>();
        services.AddScoped<IDeviceEnrollmentCommitRepository, DeviceEnrollmentCommitRepository>();
        services.AddScoped<IDeletedUserBarrierRepository, DeletedUserBarrierRepository>();
        services.AddScoped<IUserCanonicalCheckpointRepository, UserCanonicalCheckpointRepository>();
        services.AddScoped<IUserSyncFaultRepository, UserSyncFaultRepository>();

        services.AddScoped<IUserPasswordsService, UserPasswordsService>();
        services.AddScoped<IUserCustomColorService, UserCustomColorService>();
        services.AddScoped<IUserPasswordTagService, UserPasswordTagService>();
        services.AddScoped<IPasswordService, PasswordService>();
        services.AddScoped<ICustomUserColorService, CustomUserColorService>();
        services.AddScoped<IPasswordTagService, PasswordTagService>();
        services.AddScoped<IGroupService, GroupService>();
        services.AddScoped<IGroupPasswordsService, GroupPasswordsService>();
        services.AddScoped<IUserDataBundleIntegrityService, UserDataBundleIntegrityService>();
        services.AddScoped<IUserDataBundleVerificationService, UserDataBundleVerificationService>();
        services.AddScoped<IUserSnapshotBatchVerificationService, UserSnapshotBatchVerificationService>();
        services.AddScoped<IUserSyncFaultService, UserSyncFaultService>();
        services.AddScoped<IUserCanonicalHealthService, UserCanonicalHealthService>();
        services.AddScoped<IDatabaseHealthService, DatabaseHealthService>();
        services.AddScoped<IUserRecoverySessionService, UserRecoverySessionService>();
        services.AddScoped<IUserRegistrationService, UserRegistrationService>();
        services.AddScoped<IUserLoginService, UserLoginService>();
        services.AddScoped<AuthSessionService>();
        services.AddScoped<IAuthSessionService>(sp => sp.GetRequiredService<AuthSessionService>());
        services.AddScoped<IAuthenticatedSessionIssuer>(sp => sp.GetRequiredService<AuthSessionService>());
        services.AddScoped<ICredentialVerificationService, CredentialVerificationService>();
        services.AddScoped<IMasterPasswordRotationService, MasterPasswordRotationService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserSessionService, UserSessionService>();
        services.AddScoped<IUserLoginIdentityProjectionService, UserLoginIdentityProjectionService>();
        services.AddScoped<IUserLookupService, UserLookupService>();
        services.AddScoped<IUserDataReaderService, UserDataReaderService>();
        services.AddScoped<IUserDataPersistenceValidator, UserDataPersistenceValidator>();
        services.AddScoped<IUserDataWriterService, UserDataWriterService>();
        services.AddScoped<IUserAccountDeletionCleanupService, UserAccountDeletionCleanupService>();
        services.AddScoped<IUserDeletionService, UserDeletionService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRememberMeService, RememberMeService>();
        services.AddScoped<IUserProfileService, UserProfileService>();
        services.AddScoped<LocalUserDeviceLinkManager>();
        services.AddScoped<UserDeviceAccessor>();
        services.AddScoped<UserDeviceMetadataEditor>();
        services.AddScoped<ILocalDeviceSettingsService, LocalDeviceSettingsService>();
        services.AddScoped<IUserDeviceQueryService, UserDeviceQueryService>();
        services.AddScoped<IUserDeviceSettingsService, UserDeviceSettingsService>();
        services.AddScoped<IUserDeviceDisconnectionService, UserDeviceDisconnectionService>();
        services.AddScoped<IDeviceService, DeviceService>();
        services.AddScoped<IDeviceSecurityService, DeviceSecurityService>();

        services.AddSingleton<IKeyVaultService, KeyVaultService>();
        services.AddSingleton<IUserLifecycleCoordinator, UserLifecycleCoordinator>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISyncVersionClockService, SyncVersionClockService>();
        services.AddMemoryCache();
        services.AddSingleton<SafeMemoryCache>();
        services.AddSingleton<IDataCachingService, DataCachingService>();
        services.AddSingleton<ITokenService, TokenService>();

        services.AddSingleton<IDeviceIdentityService, DeviceIdentityService>();
        services.AddSingleton<ISyncTransportClientService, TcpSyncClientService>();
        services.AddSingleton<ISyncDeviceIdentityService, SyncDeviceIdentityService>();
        services.AddSingleton<IDiscoveredDeviceEndpointRegistry, DiscoveredDeviceEndpointRegistry>();
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
        services.AddScoped<IUserDataRecoveryCoordinator, UserDataRecoveryCoordinator>();
        services.AddScoped<IUserSnapshotPublisherService, UserSnapshotPublisherService>();
        services.AddScoped<IUserSnapshotInboxService, UserSnapshotInboxService>();
        services.AddScoped<IUserSnapshotMergeCoordinator, UserSnapshotMergeCoordinator>();
        services.AddScoped<IUserSnapshotAntiEntropyService, UserSnapshotAntiEntropyService>();
        services.AddScoped<IUserMembershipAuthorizationService, UserMembershipAuthorizationService>();
        services.AddScoped<IUserControlOperationWriterService, UserControlOperationWriterService>();
        services.AddScoped<IUserControlOperationInboxService, UserControlOperationInboxService>();
        services.AddScoped<IUserControlOperationAntiEntropyService, UserControlOperationAntiEntropyService>();
        services.AddScoped<IUserSyncKeyResolverService, UserSyncKeyResolverService>();
        services.AddScoped<IUserTombstoneGarbageCollector, UserTombstoneGarbageCollector>();
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

        services.AddSingleton<UserDataRecoveryScheduler>();
        services.AddSingleton<IUserDataRecoveryScheduler>(sp => sp.GetRequiredService<UserDataRecoveryScheduler>());
        services.AddSingleton<UserDataRecoveryHostedService>();
        services.AddSingleton<IBackendHostedService>(sp => sp.GetRequiredService<UserDataRecoveryHostedService>());

        services.AddSingleton<ExpiredEntriesPurgeHostedService>();
        services.AddSingleton<IBackendHostedService>(sp => sp.GetRequiredService<ExpiredEntriesPurgeHostedService>());
        services.AddSingleton<LocalDeviceCleanupHostedService>();
        services.AddSingleton<IBackendHostedService>(sp => sp.GetRequiredService<LocalDeviceCleanupHostedService>());
        services.AddSingleton<PendingUserControlOperationRecoveryHostedService>();
        services.AddSingleton<IBackendHostedService>(sp => sp.GetRequiredService<PendingUserControlOperationRecoveryHostedService>());

        services.AddSingleton<SyncPeerProtocolHandler>();
        services.AddSingleton<SyncDeviceIdentityWarmupHostedService>();
        services.AddSingleton<TcpSyncServerHostedService>();
        services.AddSingleton<SyncNetworkRefreshHostedService>();

        services.AddSingleton<ISyncControlledHostedService>(sp => sp.GetRequiredService<SyncDeviceIdentityWarmupHostedService>());
        services.AddSingleton<ISyncControlledHostedService>(sp => sp.GetRequiredService<TcpSyncServerHostedService>());
        services.AddSingleton<ISyncControlledHostedService>(sp => sp.GetRequiredService<LocalDiscoveryHostedService>());
        services.AddSingleton<ISyncControlledHostedService>(sp => sp.GetRequiredService<SyncNetworkRefreshHostedService>());


        return services;
    }
}