using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Services;
using PasswordManagerLocalBackend.Sync;
using PasswordManagerLocalTest.Fakes;
using PasswordManagerLocalTest.TestInfrastructure;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocalTest.Backend.Services;

[TestClass]
public sealed class SyncQueueServiceIntegrationTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task EnqueueUserUpdate_QueuesOnlyEnabledRemoteDevice_AndDoesNotDuplicatePendingWork()
    {
        await using var database = await SqliteIntegrationTestDatabase.CreateAsync();
        var seeded = await SeedUserRoutesAsync(database, enabledRemoteCount: 1, disabledRemoteCount: 1);
        var service = CreateService(database, seeded.Identity, new FakeSyncAuthorizationService());
        var originalModifiedAt = seeded.User.LastModifiedAt;

        await service.EnqueueAsync(new SyncItem
        {
            ModelId = seeded.User.UId,
            ModelType = SyncModelType.User,
            ChangeType = SyncChangeType.Updated
        });
        await service.EnqueueAsync(new SyncItem
        {
            ModelId = seeded.User.UId,
            ModelType = SyncModelType.User,
            ChangeType = SyncChangeType.Updated
        });

        var syncItem = await database.Db.SyncItems.SingleAsync();
        var queueItems = await database.Db.SyncQueueItems.ToListAsync();
        MSTestAssert.HasCount(1, queueItems);
        MSTestAssert.AreEqual(seeded.EnabledRemotes.Single().Id, queueItems[0].DeviceId);
        MSTestAssert.AreEqual(syncItem.Id, queueItems[0].SyncItemId);
        MSTestAssert.IsTrue(seeded.User.LastModifiedAt > originalModifiedAt);
        seeded.User.VerifyIntegrity();
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task EnqueuePropagation_ExcludesSourceDevice_PreservesRemoteTimestamp_AndDoesNotTouchLocalModel()
    {
        await using var database = await SqliteIntegrationTestDatabase.CreateAsync();
        var seeded = await SeedUserRoutesAsync(database, enabledRemoteCount: 2, disabledRemoteCount: 0);
        var service = CreateService(database, seeded.Identity, new FakeSyncAuthorizationService());
        var originalModifiedAt = seeded.User.LastModifiedAt;
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds();
        var source = seeded.EnabledRemotes[0];
        var target = seeded.EnabledRemotes[1];

        await service.EnqueuePropagationAsync(new SyncItem
        {
            ModelId = seeded.User.UId,
            ModelType = SyncModelType.User,
            ChangeType = SyncChangeType.Updated
        }, source.Id, timestamp);

        var syncItem = await database.Db.SyncItems.SingleAsync();
        var queueItems = await database.Db.SyncQueueItems.ToListAsync();
        MSTestAssert.AreEqual(timestamp, syncItem.ChangedAtTs);
        MSTestAssert.HasCount(1, queueItems);
        MSTestAssert.AreEqual(target.Id, queueItems[0].DeviceId);
        MSTestAssert.AreEqual(originalModifiedAt, seeded.User.LastModifiedAt);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task EnqueueDeletedUserDevice_QueuesRemovedDeviceAndOtherActiveDevices()
    {
        await using var database = await SqliteIntegrationTestDatabase.CreateAsync();
        var seeded = await SeedUserRoutesAsync(database, enabledRemoteCount: 2, disabledRemoteCount: 0);
        var removed = seeded.EnabledRemotes[0];
        var other = seeded.EnabledRemotes[1];
        var link = await database.Db.UserDevices.SingleAsync(ud => ud.UserId == seeded.User.UId && ud.DeviceId == removed.Id);
        link.IsDeleted = true;
        link.IsSyncOn = false;
        link.DeletedAt = DateTimeOffset.UtcNow;
        link.GenerateIntegrityHash();
        await database.Db.SaveChangesAsync();
        var service = CreateService(database, seeded.Identity, new FakeSyncAuthorizationService());

        await service.EnqueueAsync(new SyncItem
        {
            ModelId = SyncIdentityUtil.BuildUserDeviceModelId(seeded.User.UId, removed.Id),
            ModelType = SyncModelType.UserDevice,
            ChangeType = SyncChangeType.Deleted
        });

        var queueItems = await database.Db.SyncQueueItems.OrderBy(item => item.DeviceId).ToListAsync();
        MSTestAssert.HasCount(2, queueItems);
        CollectionAssert.AreEquivalent(new[] { removed.Id, other.Id }, queueItems.Select(item => item.DeviceId).ToArray());
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task EnqueuePropagationDeletedUserDevice_DoesNotQueueRemovedDevice()
    {
        await using var database = await SqliteIntegrationTestDatabase.CreateAsync();
        var seeded = await SeedUserRoutesAsync(database, enabledRemoteCount: 3, disabledRemoteCount: 0);
        var removed = seeded.EnabledRemotes[0];
        var source = seeded.EnabledRemotes[1];
        var target = seeded.EnabledRemotes[2];
        var link = await database.Db.UserDevices.SingleAsync(ud => ud.UserId == seeded.User.UId && ud.DeviceId == removed.Id);
        link.IsDeleted = true;
        link.IsSyncOn = false;
        link.DeletedAt = DateTimeOffset.UtcNow;
        link.GenerateIntegrityHash();
        await database.Db.SaveChangesAsync();
        var service = CreateService(database, seeded.Identity, new FakeSyncAuthorizationService());

        await service.EnqueuePropagationAsync(new SyncItem
        {
            ModelId = SyncIdentityUtil.BuildUserDeviceModelId(seeded.User.UId, removed.Id),
            ModelType = SyncModelType.UserDevice,
            ChangeType = SyncChangeType.Deleted
        }, source.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        var queueItem = await database.Db.SyncQueueItems.SingleAsync();
        MSTestAssert.AreEqual(target.Id, queueItem.DeviceId);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task EnqueueDeletedGroup_CreatesTombstone_AndQueuesEligibleDevice()
    {
        await using var database = await SqliteIntegrationTestDatabase.CreateAsync();
        var seeded = await SeedUserRoutesAsync(database, enabledRemoteCount: 1, disabledRemoteCount: 0);
        var group = new Group
        {
            Id = Guid.NewGuid(),
            EncryptedPayload = [4, 5, 6],
            LastModifiedAt = DateTimeOffset.UtcNow.AddMinutes(-2)
        };
        group.Users.Add(seeded.User);
        group.GenerateIntegrityHash();
        database.Db.Groups.Add(group);
        await database.Db.SaveChangesAsync();
        var service = CreateService(database, seeded.Identity, new FakeSyncAuthorizationService());

        await service.EnqueueAsync(new SyncItem
        {
            ModelId = group.Id,
            ModelType = SyncModelType.Group,
            ChangeType = SyncChangeType.Deleted
        });

        var tombstone = await database.Db.SyncTombstones.SingleAsync();
        var queueItem = await database.Db.SyncQueueItems.SingleAsync();
        var syncItem = await database.Db.SyncItems.SingleAsync();
        MSTestAssert.AreEqual(group.Id, tombstone.ModelId);
        MSTestAssert.AreEqual(SyncModelType.Group, tombstone.ModelType);
        MSTestAssert.AreEqual(syncItem.ChangedAtTs, tombstone.DeletedAtTs);
        MSTestAssert.AreEqual(seeded.EnabledRemotes.Single().Id, queueItem.DeviceId);
        MSTestAssert.AreEqual(SyncChangeType.Deleted, syncItem.ChangeType);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task EnqueueForDevice_WhenAuthorizationDenies_PersistsSyncItemWithoutQueueEntry()
    {
        await using var database = await SqliteIntegrationTestDatabase.CreateAsync();
        var seeded = await SeedUserRoutesAsync(database, enabledRemoteCount: 1, disabledRemoteCount: 0);
        var authorization = new FakeSyncAuthorizationService { CanSend = false };
        var service = CreateService(database, seeded.Identity, authorization);

        await service.EnqueueForDeviceAsync(new SyncItem
        {
            ModelId = seeded.User.UId,
            ModelType = SyncModelType.User,
            ChangeType = SyncChangeType.Updated
        }, seeded.EnabledRemotes.Single().Id);

        MSTestAssert.AreEqual(1, await database.Db.SyncItems.CountAsync());
        MSTestAssert.AreEqual(0, await database.Db.SyncQueueItems.CountAsync());
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task EnqueuePendingItem_MergesCreatedThenUpdatedAsCreated_AndDeletionWins()
    {
        await using var database = await SqliteIntegrationTestDatabase.CreateAsync();
        var seeded = await SeedUserRoutesAsync(database, enabledRemoteCount: 1, disabledRemoteCount: 0);
        var service = CreateService(database, seeded.Identity, new FakeSyncAuthorizationService());

        await service.EnqueueAsync(new SyncItem
        {
            ModelId = seeded.User.UId,
            ModelType = SyncModelType.User,
            ChangeType = SyncChangeType.Created
        });
        await service.EnqueueAsync(new SyncItem
        {
            ModelId = seeded.User.UId,
            ModelType = SyncModelType.User,
            ChangeType = SyncChangeType.Updated
        });

        var syncItem = await database.Db.SyncItems.SingleAsync();
        MSTestAssert.AreEqual(SyncChangeType.Created, syncItem.ChangeType);

        await service.EnqueueAsync(new SyncItem
        {
            ModelId = seeded.User.UId,
            ModelType = SyncModelType.User,
            ChangeType = SyncChangeType.Deleted
        });

        MSTestAssert.AreEqual(SyncChangeType.Deleted, syncItem.ChangeType);
        MSTestAssert.AreEqual(1, await database.Db.SyncQueueItems.CountAsync());
    }

    private static SyncQueueService CreateService(
        SqliteIntegrationTestDatabase database,
        FakeDeviceIdentityService identity,
        FakeSyncAuthorizationService authorization) =>
        new(
            database.SyncQueue,
            database.SyncItems,
            database.Users,
            database.Groups,
            database.Devices,
            database.UserDevices,
            database.LocalUserDevices,
            database.Tombstones,
            new FakeSyncDeviceIdentityService(),
            new DiscoveredDeviceEndpointCache(),
            new FakeDeviceSyncTaskService(),
            identity,
            authorization,
            database.UnitOfWork);

    private static async Task<SeededUserRoutes> SeedUserRoutesAsync(
        SqliteIntegrationTestDatabase database,
        int enabledRemoteCount,
        int disabledRemoteCount)
    {
        var localId = Guid.NewGuid();
        var identityModel = new LocalDeviceIdentity
        {
            Id = localId,
            AgreementPrivateKeyBlob = [1],
            SignPrivateKeyBlob = [2],
            PFXCertificate = [3],
            DeviceType = DeviceType.WindowsPc,
            IsSyncOn = true
        };
        identityModel.GenerateIntegrityHash();
        var user = new User
        {
            UId = Guid.NewGuid(),
            UsernameHash = [1],
            UsernameSalt = [2],
            PasswordSalt = [3],
            EncryptedPayload = [4],
            LastModifiedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        user.GenerateIntegrityHash();
        database.Db.LocalDeviceIdentities.Add(identityModel);
        database.Db.Users.Add(user);
        database.Db.LocalUserDevices.Add(new LocalUserDevice
        {
            UserId = user.UId,
            LocalDeviceIdentityId = localId,
            IsSyncOn = true
        });

        var enabledRemotes = new List<Device>();
        for (var i = 0; i < enabledRemoteCount; i++)
        {
            var device = CreateRemoteDevice($"AA{i:D2}");
            enabledRemotes.Add(device);
            database.Db.Devices.Add(device);
            database.Db.UserDevices.Add(new UserDevice
            {
                UserId = user.UId,
                DeviceId = device.Id,
                IsSyncOn = true,
                IsDeleted = false
            });
        }

        var disabledRemotes = new List<Device>();
        for (var i = 0; i < disabledRemoteCount; i++)
        {
            var device = CreateRemoteDevice($"BB{i:D2}");
            disabledRemotes.Add(device);
            database.Db.Devices.Add(device);
            database.Db.UserDevices.Add(new UserDevice
            {
                UserId = user.UId,
                DeviceId = device.Id,
                IsSyncOn = false,
                IsDeleted = false
            });
        }

        await database.Db.SaveChangesAsync();
        var identity = new FakeDeviceIdentityService
        {
            IsInitialized = true,
            IsSyncOn = true,
            LocalDeviceId = localId,
            FingerprintHex = "LOCAL",
            SignPublicKey = Enumerable.Repeat((byte)9, 32).ToArray()
        };
        return new SeededUserRoutes(user, enabledRemotes, disabledRemotes, identity);
    }

    private static Device CreateRemoteDevice(string fingerprint)
    {
        var device = new Device
        {
            Id = Guid.NewGuid(),
            PublicKey = Enumerable.Repeat((byte)1, 32).ToArray(),
            SignPublicKey = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray(),
            TlsCertFingerprint = fingerprint,
            DeviceType = DeviceType.WindowsPc,
            IsTrusted = true,
            IsBlocked = false
        };
        device.GenerateIntegrityHash();
        return device;
    }

    private sealed record SeededUserRoutes(
        User User,
        IReadOnlyList<Device> EnabledRemotes,
        IReadOnlyList<Device> DisabledRemotes,
        FakeDeviceIdentityService Identity);
}
