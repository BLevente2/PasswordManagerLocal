using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSec.Cryptography;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using PasswordManagerLocal.Test.Fakes;
using PasswordManagerLocal.Test.TestInfrastructure;
using System.Security.Cryptography;
using System.Text.Json;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.Backend.Services;

[TestClass]
public sealed class UserSnapshotMergeCoordinatorTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task TryMergePendingAsync_MultipleOrigins_DeletesExactRowsPublishesCoverageAndQueuesAtomically()
    {
        await using var database = await SqliteIntegrationTestDatabase.CreateAsync();
        using var localSigningKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters());
        using var firstSigningKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters());
        using var secondSigningKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters());
        var localIdentity = CreateIdentity(Guid.NewGuid(), Guid.NewGuid(), localSigningKey);
        var firstOrigin = (DeviceId: Guid.NewGuid(), InstanceId: Guid.NewGuid());
        var secondOrigin = (DeviceId: Guid.NewGuid(), InstanceId: Guid.NewGuid());
        var user = await AddUserAndMembershipAsync(
            database,
            localIdentity,
            (firstOrigin.DeviceId, firstSigningKey),
            (secondOrigin.DeviceId, secondSigningKey));

        await AddPendingAsync(database, CreateEnvelope(user, firstOrigin.DeviceId, firstOrigin.InstanceId, 2, firstSigningKey, 0x21));
        await AddPendingAsync(database, CreateEnvelope(user, secondOrigin.DeviceId, secondOrigin.InstanceId, 5, secondSigningKey, 0x52));
        await database.UnitOfWork.SaveChangesAsync();

        var lifecycle = new UserLifecycleCoordinator();
        var bundleSync = new FakeUserDataBundleSyncService
        {
            Handler = (canonical, snapshots, _, _) =>
            {
                var now = DateTimeOffset.UtcNow.AddMinutes(1);
                canonical.EncryptedGeneralUserDataPayload = [0xEE, 0x01];
                canonical.GeneralUserDataLastModifiedAt = now;
                canonical.UserDataLastModifiedAt = now;
                canonical.LastModifiedAt = now;
                canonical.GenerateIntegrityHash();
                return Task.FromResult(new UserSnapshotMergeBatchResult(
                    CanonicalChanged: true,
                    snapshots.Select(snapshot => new UserSnapshotMergeEntryResult(
                        snapshot.OriginDeviceId,
                        snapshot.OriginInstanceId,
                        snapshot.OriginRevision,
                        Verified: true)).ToArray()));
            }
        };
        var publisher = new UserSnapshotPublisherService(
            database.UserSyncSnapshots,
            database.UserSyncStates,
            database.UserRevisionKnowledge,
            localIdentity,
            database.UnitOfWork,
            lifecycle);
        var queue = new FakeSyncQueueWriterService();
        var activation = new FakeSyncQueueService();
        var coordinator = new UserSnapshotMergeCoordinator(
            database.Users,
            new FakeUserMembershipAuthorizationService(),
            database.UserSyncSnapshots,
            database.UserRevisionKnowledge,
            bundleSync,
            publisher,
            queue,
            activation,
            database.UnitOfWork,
            lifecycle);

        using var key = EncryptionKey.Create();
        var merged = await coordinator.TryMergePendingAsync(user.UId, key);

        database.Db.ChangeTracker.Clear();
        var rows = await database.Db.UserSyncSnapshots.Where(snapshot => snapshot.UserId == user.UId).ToListAsync();
        var published = rows.Single(snapshot => snapshot.Status == UserSyncSnapshotStatus.LocalPublished);
        var publishedEnvelope = JsonSerializer.Deserialize(
            published.EnvelopePayload,
            BackendJsonSerializerContext.Default.UserSnapshotEnvelope)
            ?? throw new InvalidDataException("The published test snapshot envelope is invalid.");
        var firstKnowledge = await database.UserRevisionKnowledge.GetAsync(
            user.UId, firstOrigin.DeviceId, firstOrigin.InstanceId, user.KeyEpoch);
        var secondKnowledge = await database.UserRevisionKnowledge.GetAsync(
            user.UId, secondOrigin.DeviceId, secondOrigin.InstanceId, user.KeyEpoch);
        var localKnowledge = await database.UserRevisionKnowledge.GetAsync(
            user.UId, localIdentity.LocalDeviceId, localIdentity.OriginInstanceId, user.KeyEpoch);
        var reloadedUser = await database.Users.GetByIdAsync(user.UId);

        MSTestAssert.IsTrue(merged);
        MSTestAssert.IsFalse(rows.Any(snapshot => snapshot.Status == UserSyncSnapshotStatus.Pending));
        MSTestAssert.HasCount(1, rows);
        MSTestAssert.IsNotNull(firstKnowledge);
        MSTestAssert.IsNotNull(secondKnowledge);
        MSTestAssert.IsNotNull(localKnowledge);
        MSTestAssert.AreEqual(2L, firstKnowledge.HighestMergedRevision);
        MSTestAssert.AreEqual(5L, secondKnowledge.HighestMergedRevision);
        MSTestAssert.AreEqual(1L, localKnowledge.HighestMergedRevision);
        MSTestAssert.IsTrue(publishedEnvelope.Coverage.Any(item =>
            item.OriginDeviceId == firstOrigin.DeviceId && item.OriginRevision == 2));
        MSTestAssert.IsTrue(publishedEnvelope.Coverage.Any(item =>
            item.OriginDeviceId == secondOrigin.DeviceId && item.OriginRevision == 5));
        MSTestAssert.HasCount(1, queue.EnqueuedItems);
        MSTestAssert.AreEqual(SyncModelType.User, queue.EnqueuedItems[0].ModelType);
        MSTestAssert.AreEqual(user.UId, queue.EnqueuedItems[0].ModelId);
        MSTestAssert.AreEqual(1, activation.ActivatePendingSyncsCalls);
        MSTestAssert.IsNotNull(reloadedUser);
        CollectionAssert.AreEqual(new byte[] { 0xEE, 0x01 }, reloadedUser.EncryptedGeneralUserDataPayload);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task TryMergePendingAsync_InvalidOriginIsQuarantinedWithoutDeletingUnrelatedValidCandidate()
    {
        await using var database = await SqliteIntegrationTestDatabase.CreateAsync();
        using var localSigningKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters());
        using var validSigningKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters());
        using var trustedButDifferentKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters());
        using var invalidEnvelopeKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters());
        var localIdentity = CreateIdentity(Guid.NewGuid(), Guid.NewGuid(), localSigningKey);
        var validOrigin = (DeviceId: Guid.NewGuid(), InstanceId: Guid.NewGuid());
        var invalidOrigin = (DeviceId: Guid.NewGuid(), InstanceId: Guid.NewGuid());
        var user = await AddUserAndMembershipAsync(
            database,
            localIdentity,
            (validOrigin.DeviceId, validSigningKey),
            (invalidOrigin.DeviceId, trustedButDifferentKey));

        await AddPendingAsync(database, CreateEnvelope(user, validOrigin.DeviceId, validOrigin.InstanceId, 3, validSigningKey, 0x33));
        await AddPendingAsync(database, CreateEnvelope(user, invalidOrigin.DeviceId, invalidOrigin.InstanceId, 4, invalidEnvelopeKey, 0x44));
        await database.UnitOfWork.SaveChangesAsync();

        var lifecycle = new UserLifecycleCoordinator();
        var publisher = new UserSnapshotPublisherService(
            database.UserSyncSnapshots,
            database.UserSyncStates,
            database.UserRevisionKnowledge,
            localIdentity,
            database.UnitOfWork,
            lifecycle);
        var coordinator = new UserSnapshotMergeCoordinator(
            database.Users,
            new FakeUserMembershipAuthorizationService(),
            database.UserSyncSnapshots,
            database.UserRevisionKnowledge,
            new FakeUserDataBundleSyncService(),
            publisher,
            new FakeSyncQueueWriterService(),
            new FakeSyncQueueService(),
            database.UnitOfWork,
            lifecycle);

        using var key = EncryptionKey.Create();
        var merged = await coordinator.TryMergePendingAsync(user.UId, key);

        database.Db.ChangeTracker.Clear();
        var rows = await database.Db.UserSyncSnapshots.Where(snapshot => snapshot.UserId == user.UId).ToListAsync();
        var quarantined = rows.Single(snapshot => snapshot.Status == UserSyncSnapshotStatus.Quarantined);
        MSTestAssert.IsTrue(merged);
        MSTestAssert.AreEqual(invalidOrigin.DeviceId, quarantined.OriginDeviceId);
        MSTestAssert.IsFalse(string.IsNullOrWhiteSpace(quarantined.QuarantineReason));
        MSTestAssert.IsFalse(rows.Any(snapshot =>
            snapshot.OriginDeviceId == validOrigin.DeviceId && snapshot.Status == UserSyncSnapshotStatus.Pending));
        MSTestAssert.IsTrue(rows.Any(snapshot => snapshot.Status == UserSyncSnapshotStatus.LocalPublished));
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task TryMergePendingAsync_WhenCanonicalCannotBeOpened_KeepsPendingAndDoesNotQueue()
    {
        await using var database = await SqliteIntegrationTestDatabase.CreateAsync();
        using var localSigningKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters());
        using var originSigningKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters());
        var localIdentity = CreateIdentity(Guid.NewGuid(), Guid.NewGuid(), localSigningKey);
        var origin = (DeviceId: Guid.NewGuid(), InstanceId: Guid.NewGuid());
        var user = await AddUserAndMembershipAsync(database, localIdentity, (origin.DeviceId, originSigningKey));
        var originalGeneral = user.EncryptedGeneralUserDataPayload.ToArray();
        await AddPendingAsync(database, CreateEnvelope(user, origin.DeviceId, origin.InstanceId, 9, originSigningKey, 0x99));
        await database.UnitOfWork.SaveChangesAsync();

        var lifecycle = new UserLifecycleCoordinator();
        var bundleSync = new FakeUserDataBundleSyncService
        {
            Handler = (_, _, _, _) => throw new CryptographicException("The active key cannot open canonical state.")
        };
        var queue = new FakeSyncQueueWriterService();
        var coordinator = new UserSnapshotMergeCoordinator(
            database.Users,
            new FakeUserMembershipAuthorizationService(),
            database.UserSyncSnapshots,
            database.UserRevisionKnowledge,
            bundleSync,
            new UserSnapshotPublisherService(
                database.UserSyncSnapshots,
                database.UserSyncStates,
                database.UserRevisionKnowledge,
                localIdentity,
                database.UnitOfWork,
                lifecycle),
            queue,
            new FakeSyncQueueService(),
            database.UnitOfWork,
            lifecycle);

        using var key = EncryptionKey.Create();
        var merged = await coordinator.TryMergePendingAsync(user.UId, key);

        database.Db.ChangeTracker.Clear();
        var pending = await database.Db.UserSyncSnapshots.SingleAsync(snapshot =>
            snapshot.UserId == user.UId && snapshot.Status == UserSyncSnapshotStatus.Pending);
        var reloadedUser = await database.Users.GetByIdAsync(user.UId);
        MSTestAssert.IsFalse(merged);
        MSTestAssert.AreEqual(9L, pending.OriginRevision);
        MSTestAssert.HasCount(0, queue.EnqueuedItems);
        MSTestAssert.IsNotNull(reloadedUser);
        CollectionAssert.AreEqual(originalGeneral, reloadedUser.EncryptedGeneralUserDataPayload);
    }

    private static async Task<User> AddUserAndMembershipAsync(
        SqliteIntegrationTestDatabase database,
        FakeDeviceIdentityService localIdentity,
        params (Guid DeviceId, Key SigningKey)[] remoteDevices)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            UId = Guid.NewGuid(),
            UsernameHash = [0x01],
            UsernameSalt = [0x02],
            PasswordSalt = [0x03],
            EncryptedPayload = [0x10],
            EncryptedGeneralUserDataPayload = [0x20],
            EncryptedUserPasswordsDataPayload = [0x30],
            EncryptedUserDevicesDataPayload = [0x40],
            KeyEpoch = 1,
            MembershipEpoch = 1,
            LastModifiedAt = now,
            UserDataLastModifiedAt = now,
            GeneralUserDataLastModifiedAt = now,
            UserPasswordsDataLastModifiedAt = now,
            UserDevicesDataLastModifiedAt = now
        };
        user.GenerateIntegrityHash();
        await database.Users.AddAsync(user);

        var localDevice = CreateTrustedDevice(localIdentity.LocalDeviceId, localIdentity.SignPublicKey);
        await database.Devices.AddAsync(localDevice);
        await AddLinkAsync(database, user.UId, localDevice.Id);
        foreach (var remote in remoteDevices)
        {
            var publicKey = remote.SigningKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);
            var device = CreateTrustedDevice(remote.DeviceId, publicKey);
            await database.Devices.AddAsync(device);
            await AddLinkAsync(database, user.UId, device.Id);
        }

        await database.UnitOfWork.SaveChangesAsync();
        database.Db.ChangeTracker.Clear();
        return await database.Users.GetByIdAsync(user.UId)
            ?? throw new InvalidOperationException("The test user could not be reloaded.");
    }

    private static Device CreateTrustedDevice(Guid id, byte[] signingPublicKey)
    {
        var device = new Device
        {
            Id = id,
            SignPublicKey = signingPublicKey.ToArray(),
            PublicKey = [0x01],
            TlsCertFingerprint = id.ToString("N"),
            IsTrusted = true,
            IsBlocked = false
        };
        device.GenerateIntegrityHash();
        return device;
    }

    private static async Task AddLinkAsync(SqliteIntegrationTestDatabase database, Guid userId, Guid deviceId)
    {
        var link = new UserDevice
        {
            UserId = userId,
            DeviceId = deviceId,
            IsSyncOn = true,
            IsDeleted = false,
            LastModifiedAt = DateTimeOffset.UtcNow
        };
        link.GenerateIntegrityHash();
        await database.UserDevices.AddAsync(link);
    }

    private static async Task AddPendingAsync(
        SqliteIntegrationTestDatabase database,
        UserSnapshotEnvelope envelope)
    {
        var serialized = JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            BackendJsonSerializerContext.Default.UserSnapshotEnvelope);
        await database.UserSyncSnapshots.AddAsync(new UserSyncSnapshot
        {
            UserId = envelope.UserId,
            OriginDeviceId = envelope.OriginDeviceId,
            OriginInstanceId = envelope.OriginInstanceId,
            OriginRevision = envelope.OriginRevision,
            UserKeyEpoch = envelope.UserKeyEpoch,
            MembershipEpoch = envelope.MembershipEpoch,
            CreatedAtUtc = envelope.CreatedAtUtc,
            ReceivedAtUtc = DateTimeOffset.UtcNow,
            SnapshotHash = envelope.SnapshotHash.ToArray(),
            OriginSignPublicKey = envelope.OriginSignPublicKey.ToArray(),
            OriginSignature = envelope.OriginSignature.ToArray(),
            EnvelopePayload = serialized,
            Status = UserSyncSnapshotStatus.Pending
        });
    }

    private static UserSnapshotEnvelope CreateEnvelope(
        User user,
        Guid originDeviceId,
        Guid originInstanceId,
        long revision,
        Key signingKey,
        byte marker)
    {
        var createdAt = DateTimeOffset.UtcNow.AddSeconds(revision);
        var payload = new UserSyncPayload
        {
            UId = user.UId,
            UsernameHash = user.UsernameHash.ToArray(),
            UsernameSalt = user.UsernameSalt.ToArray(),
            PasswordSalt = user.PasswordSalt.ToArray(),
            EncryptedPayload = [marker, 0x01],
            EncryptedGeneralUserDataPayload = [marker, 0x02],
            EncryptedUserPasswordsDataPayload = [marker, 0x03],
            EncryptedUserDevicesDataPayload = [marker, 0x04],
            UserDataLastModifiedAt = createdAt,
            GeneralUserDataLastModifiedAt = createdAt,
            UserPasswordsDataLastModifiedAt = createdAt,
            UserDevicesDataLastModifiedAt = createdAt,
            GroupIds = [],
            DeviceIds = [originDeviceId]
        };
        payload.IntegrityHash = SyncCryptoUtil.CalculateUserHash(payload, createdAt.ToUnixTimeMilliseconds());
        var envelope = new UserSnapshotEnvelope
        {
            UserId = user.UId,
            OriginDeviceId = originDeviceId,
            OriginInstanceId = originInstanceId,
            OriginRevision = revision,
            UserKeyEpoch = user.KeyEpoch,
            MembershipEpoch = user.MembershipEpoch,
            CreatedAtUtc = createdAt,
            User = payload
        };
        UserSnapshotEnvelopeUtil.FillOriginAuthentication(
            envelope,
            CreateIdentity(originDeviceId, originInstanceId, signingKey));
        return envelope;
    }

    private static FakeDeviceIdentityService CreateIdentity(Guid deviceId, Guid instanceId, Key signingKey) =>
        new()
        {
            LocalDeviceId = deviceId,
            OriginInstanceId = instanceId,
            SignPublicKey = signingKey.PublicKey.Export(KeyBlobFormat.RawPublicKey),
            SignHandler = bytes => SignatureAlgorithm.Ed25519.Sign(signingKey, bytes)
        };
}

