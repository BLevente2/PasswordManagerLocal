using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSec.Cryptography;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Persistence;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Test.Fakes;
using PasswordManagerLocal.Test.TestInfrastructure;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.Backend.Services;

[TestClass]
public sealed class UserSnapshotInboxServiceTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task StoreAsync_WithoutActiveKey_DurablyStoresPendingAndDoesNotReplaceCanonicalBlobs()
    {
        await using var database = await SqliteIntegrationTestDatabase.CreateAsync();
        var user = await AddCanonicalUserAsync(database);
        var originalGeneral = user.EncryptedGeneralUserDataPayload.ToArray();
        var originalPasswords = user.EncryptedUserPasswordsDataPayload.ToArray();
        var originalDevices = user.EncryptedUserDevicesDataPayload.ToArray();
        var localIdentity = CreateUnsignedIdentity(Guid.NewGuid(), Guid.NewGuid());
        var service = new UserSnapshotInboxService(
            database.Users,
            database.UserSyncSnapshots,
            database.UserRevisionKnowledge,
            localIdentity,
            database.UnitOfWork,
            new UserLifecycleCoordinator(),
            new FakeUserMembershipAuthorizationService());

        using var originKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters());
        var originDeviceId = Guid.NewGuid();
        var originInstanceId = Guid.NewGuid();
        var envelope = CreateSignedEnvelope(user, originDeviceId, originInstanceId, 7, originKey, marker: 0x77);
        var relayDeviceId = Guid.NewGuid();

        var receipt = await service.StoreAsync(envelope, relayDeviceId);

        // Read through a fresh DbContext to prove StoreAsync does not report durable receipt
        // until the pending snapshot and revision knowledge have committed.
        var restartOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(database.Db.Database.GetDbConnection())
            .Options;
        await using var restartedDb = new AppDbContext(restartOptions);
        var stored = await restartedDb.UserSyncSnapshots.SingleOrDefaultAsync(snapshot =>
            snapshot.UserId == user.UId &&
            snapshot.OriginDeviceId == originDeviceId &&
            snapshot.OriginInstanceId == originInstanceId &&
            snapshot.UserKeyEpoch == user.KeyEpoch);
        var reloadedUser = await restartedDb.Users.SingleOrDefaultAsync(candidate => candidate.UId == user.UId);

        MSTestAssert.AreEqual(UserSnapshotReceiptState.StoredPending, receipt.State);
        MSTestAssert.IsNotNull(stored);
        MSTestAssert.AreEqual(UserSyncSnapshotStatus.Pending, stored.Status);
        MSTestAssert.AreEqual(7L, stored.OriginRevision);
        MSTestAssert.AreEqual(originDeviceId, stored.OriginDeviceId);
        MSTestAssert.AreEqual(originInstanceId, stored.OriginInstanceId);
        MSTestAssert.AreEqual(relayDeviceId, stored.LastReceivedFromDeviceId);
        CollectionAssert.AreEqual(envelope.SnapshotHash, stored.SnapshotHash);
        MSTestAssert.IsNotNull(reloadedUser);
        CollectionAssert.AreEqual(originalGeneral, reloadedUser.EncryptedGeneralUserDataPayload);
        CollectionAssert.AreEqual(originalPasswords, reloadedUser.EncryptedUserPasswordsDataPayload);
        CollectionAssert.AreEqual(originalDevices, reloadedUser.EncryptedUserDevicesDataPayload);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task StoreAsync_EnforcesMonotonicRevisionIdempotenceAndOneLatestRowPerOrigin()
    {
        await using var database = await SqliteIntegrationTestDatabase.CreateAsync();
        var user = await AddCanonicalUserAsync(database);
        var service = new UserSnapshotInboxService(
            database.Users,
            database.UserSyncSnapshots,
            database.UserRevisionKnowledge,
            CreateUnsignedIdentity(Guid.NewGuid(), Guid.NewGuid()),
            database.UnitOfWork,
            new UserLifecycleCoordinator(),
            new FakeUserMembershipAuthorizationService());
        using var originKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters());
        var originDeviceId = Guid.NewGuid();
        var originInstanceId = Guid.NewGuid();

        var revisionTwo = CreateSignedEnvelope(user, originDeviceId, originInstanceId, 2, originKey, marker: 0x22);
        var first = await service.StoreAsync(revisionTwo, Guid.NewGuid());
        await database.UnitOfWork.SaveChangesAsync();

        var revisionOne = CreateSignedEnvelope(user, originDeviceId, originInstanceId, 1, originKey, marker: 0x11);
        var obsolete = await service.StoreAsync(revisionOne, Guid.NewGuid());
        var duplicate = await service.StoreAsync(revisionTwo, Guid.NewGuid());

        var revisionThree = CreateSignedEnvelope(user, originDeviceId, originInstanceId, 3, originKey, marker: 0x33);
        var replaced = await service.StoreAsync(revisionThree, Guid.NewGuid());
        await database.UnitOfWork.SaveChangesAsync();

        var rows = await database.Db.UserSyncSnapshots
            .Where(snapshot => snapshot.UserId == user.UId && snapshot.OriginDeviceId == originDeviceId)
            .ToListAsync();
        var knowledge = await database.UserRevisionKnowledge.GetAsync(
            user.UId,
            originDeviceId,
            originInstanceId,
            user.KeyEpoch);

        MSTestAssert.AreEqual(UserSnapshotReceiptState.StoredPending, first.State);
        MSTestAssert.AreEqual(UserSnapshotReceiptState.ObsoleteRevision, obsolete.State);
        MSTestAssert.AreEqual(UserSnapshotReceiptState.AlreadyStored, duplicate.State);
        MSTestAssert.AreEqual(UserSnapshotReceiptState.ReplacedOlderPending, replaced.State);
        MSTestAssert.HasCount(1, rows);
        MSTestAssert.AreEqual(3L, rows[0].OriginRevision);
        CollectionAssert.AreEqual(revisionThree.SnapshotHash, rows[0].SnapshotHash);
        MSTestAssert.IsNotNull(knowledge);
        MSTestAssert.AreEqual(3L, knowledge.HighestStoredRevision);
        MSTestAssert.AreEqual(0L, knowledge.HighestMergedRevision);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task StoreAsync_SameRevisionDifferentHash_QuarantinesOriginAndRejectsLaterOverwrite()
    {
        await using var database = await SqliteIntegrationTestDatabase.CreateAsync();
        var user = await AddCanonicalUserAsync(database);
        var service = new UserSnapshotInboxService(
            database.Users,
            database.UserSyncSnapshots,
            database.UserRevisionKnowledge,
            CreateUnsignedIdentity(Guid.NewGuid(), Guid.NewGuid()),
            database.UnitOfWork,
            new UserLifecycleCoordinator(),
            new FakeUserMembershipAuthorizationService());
        using var originKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters());
        var originDeviceId = Guid.NewGuid();
        var originInstanceId = Guid.NewGuid();

        var original = CreateSignedEnvelope(user, originDeviceId, originInstanceId, 9, originKey, marker: 0x44);
        await service.StoreAsync(original, Guid.NewGuid());
        await database.UnitOfWork.SaveChangesAsync();

        var fork = CreateSignedEnvelope(user, originDeviceId, originInstanceId, 9, originKey, marker: 0x45);
        var forkReceipt = await service.StoreAsync(fork, Guid.NewGuid());
        await database.UnitOfWork.SaveChangesAsync();

        var later = CreateSignedEnvelope(user, originDeviceId, originInstanceId, 10, originKey, marker: 0x46);
        var laterReceipt = await service.StoreAsync(later, Guid.NewGuid());
        await database.UnitOfWork.SaveChangesAsync();

        var row = await database.UserSyncSnapshots.GetAsync(
            user.UId,
            originDeviceId,
            originInstanceId,
            user.KeyEpoch);

        MSTestAssert.AreEqual(UserSnapshotReceiptState.Quarantined, forkReceipt.State);
        MSTestAssert.AreEqual(UserSnapshotReceiptState.Quarantined, laterReceipt.State);
        MSTestAssert.IsNotNull(row);
        MSTestAssert.AreEqual(UserSyncSnapshotStatus.Quarantined, row.Status);
        MSTestAssert.AreEqual(9L, row.OriginRevision);
        CollectionAssert.AreEqual(original.SnapshotHash, row.SnapshotHash);
        CollectionAssert.AreEqual(fork.SnapshotHash, row.ConflictingSnapshotHash);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task StoreAsync_ForkOfAlreadyMergedRevision_PersistsQuarantineEvidence()
    {
        await using var database = await SqliteIntegrationTestDatabase.CreateAsync();
        var user = await AddCanonicalUserAsync(database);
        var service = new UserSnapshotInboxService(
            database.Users,
            database.UserSyncSnapshots,
            database.UserRevisionKnowledge,
            CreateUnsignedIdentity(Guid.NewGuid(), Guid.NewGuid()),
            database.UnitOfWork,
            new UserLifecycleCoordinator(),
            new FakeUserMembershipAuthorizationService());
        using var originKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters());
        var originDeviceId = Guid.NewGuid();
        var originInstanceId = Guid.NewGuid();
        var merged = CreateSignedEnvelope(user, originDeviceId, originInstanceId, 5, originKey, marker: 0x51);
        await database.UserRevisionKnowledge.AddAsync(new UserRevisionKnowledge
        {
            UserId = user.UId,
            OriginDeviceId = originDeviceId,
            OriginInstanceId = originInstanceId,
            UserKeyEpoch = user.KeyEpoch,
            HighestStoredRevision = 5,
            HighestStoredSnapshotHash = merged.SnapshotHash.ToArray(),
            HighestMergedRevision = 5
        });
        await database.UnitOfWork.SaveChangesAsync();

        var fork = CreateSignedEnvelope(user, originDeviceId, originInstanceId, 5, originKey, marker: 0x52);
        var receipt = await service.StoreAsync(fork, Guid.NewGuid());

        database.Db.ChangeTracker.Clear();
        var quarantine = await database.UserSyncSnapshots.GetAsync(
            user.UId,
            originDeviceId,
            originInstanceId,
            user.KeyEpoch);

        MSTestAssert.AreEqual(UserSnapshotReceiptState.Quarantined, receipt.State);
        MSTestAssert.IsNotNull(quarantine);
        MSTestAssert.AreEqual(UserSyncSnapshotStatus.Quarantined, quarantine.Status);
        CollectionAssert.AreEqual(fork.SnapshotHash, quarantine.SnapshotHash);
        CollectionAssert.AreEqual(merged.SnapshotHash, quarantine.ConflictingSnapshotHash);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task StoreAsync_RelayedSnapshot_RetainsOriginalOriginAndOnlyUpdatesLastRelayDiagnostic()
    {
        await using var database = await SqliteIntegrationTestDatabase.CreateAsync();
        var user = await AddCanonicalUserAsync(database);
        var service = new UserSnapshotInboxService(
            database.Users,
            database.UserSyncSnapshots,
            database.UserRevisionKnowledge,
            CreateUnsignedIdentity(Guid.NewGuid(), Guid.NewGuid()),
            database.UnitOfWork,
            new UserLifecycleCoordinator(),
            new FakeUserMembershipAuthorizationService());
        using var originKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters());
        var originDeviceId = Guid.NewGuid();
        var originInstanceId = Guid.NewGuid();
        var relayB = Guid.NewGuid();
        var relayC = Guid.NewGuid();
        var envelope = CreateSignedEnvelope(user, originDeviceId, originInstanceId, 15, originKey, marker: 0x55);

        await service.StoreAsync(envelope, relayB);
        await database.UnitOfWork.SaveChangesAsync();
        var secondReceipt = await service.StoreAsync(envelope, relayC);
        await database.UnitOfWork.SaveChangesAsync();

        var row = await database.UserSyncSnapshots.GetAsync(
            user.UId,
            originDeviceId,
            originInstanceId,
            user.KeyEpoch);

        MSTestAssert.AreEqual(UserSnapshotReceiptState.AlreadyStored, secondReceipt.State);
        MSTestAssert.IsNotNull(row);
        MSTestAssert.AreEqual(originDeviceId, row.OriginDeviceId);
        MSTestAssert.AreEqual(originInstanceId, row.OriginInstanceId);
        MSTestAssert.AreEqual(15L, row.OriginRevision);
        MSTestAssert.AreEqual(relayC, row.LastReceivedFromDeviceId);
        MSTestAssert.HasCount(1, await database.Db.UserSyncSnapshots.ToListAsync());
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task StoreAsync_PeerClaimsCurrentLocalOriginIdentity_RejectsWithoutCreatingRow()
    {
        await using var database = await SqliteIntegrationTestDatabase.CreateAsync();
        var user = await AddCanonicalUserAsync(database);
        using var localKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters());
        var localDeviceId = Guid.NewGuid();
        var localInstanceId = Guid.NewGuid();
        var localIdentity = CreateSigningIdentity(localDeviceId, localInstanceId, localKey);
        var service = new UserSnapshotInboxService(
            database.Users,
            database.UserSyncSnapshots,
            database.UserRevisionKnowledge,
            localIdentity,
            database.UnitOfWork,
            new UserLifecycleCoordinator(),
            new FakeUserMembershipAuthorizationService());
        var envelope = CreateSignedEnvelope(user, localDeviceId, localInstanceId, 2, localKey, marker: 0x66);

        var receipt = await service.StoreAsync(envelope, Guid.NewGuid());
        await database.UnitOfWork.SaveChangesAsync();

        MSTestAssert.AreEqual(UserSnapshotReceiptState.Quarantined, receipt.State);
        MSTestAssert.HasCount(0, await database.Db.UserSyncSnapshots.ToListAsync());
    }

    private static async Task<User> AddCanonicalUserAsync(SqliteIntegrationTestDatabase database)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-5);
        var user = new User
        {
            UId = Guid.NewGuid(),
            UsernameHash = [0x01, 0x02],
            UsernameSalt = [0x03, 0x04],
            PasswordSalt = [0x05, 0x06],
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
        await database.UnitOfWork.SaveChangesAsync();
        return user;
    }

    private static UserSnapshotEnvelope CreateSignedEnvelope(
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
            CreateSigningIdentity(originDeviceId, originInstanceId, signingKey));
        return envelope;
    }

    private static FakeDeviceIdentityService CreateSigningIdentity(Guid deviceId, Guid instanceId, Key key) =>
        new()
        {
            LocalDeviceId = deviceId,
            OriginInstanceId = instanceId,
            SignPublicKey = key.PublicKey.Export(KeyBlobFormat.RawPublicKey),
            SignHandler = data => SignatureAlgorithm.Ed25519.Sign(key, data)
        };

    private static FakeDeviceIdentityService CreateUnsignedIdentity(Guid deviceId, Guid instanceId) =>
        new()
        {
            LocalDeviceId = deviceId,
            OriginInstanceId = instanceId
        };
}
