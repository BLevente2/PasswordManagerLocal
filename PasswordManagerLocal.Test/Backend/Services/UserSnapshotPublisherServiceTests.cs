using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSec.Cryptography;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using System.Text.Json;
using PasswordManagerLocal.Test.Fakes;
using PasswordManagerLocal.Test.TestInfrastructure;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.Backend.Services;

[TestClass]
public sealed class UserSnapshotPublisherServiceTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task GetOrCreateAsync_AssignsDurableMonotonicRevisionsAndReusesUnchangedSnapshot()
    {
        await using var database = await SqliteIntegrationTestDatabase.CreateAsync();
        var user = CreateUser();
        await database.Users.AddAsync(user);
        await database.UnitOfWork.SaveChangesAsync();
        using var signingKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters());
        var identity = CreateIdentity(signingKey);
        var service = new UserSnapshotPublisherService(
            database.UserSyncSnapshots,
            database.UserSyncStates,
            database.UserRevisionKnowledge,
            identity,
            database.UnitOfWork,
            new UserLifecycleCoordinator());

        var first = await service.GetOrCreateAsync(user);
        var firstEnvelope = JsonSerializer.Deserialize(
            first.EnvelopePayload,
            BackendJsonSerializerContext.Default.UserSnapshotEnvelope);
        var unchanged = await service.GetOrCreateAsync(user);
        var firstId = first.Id;
        var firstRevision = first.OriginRevision;
        var unchangedId = unchanged.Id;
        var unchangedRevision = unchanged.OriginRevision;

        user.EncryptedUserPasswordsDataPayload = [0x99, 0x01];
        user.UserPasswordsDataLastModifiedAt = DateTimeOffset.UtcNow.AddMinutes(1);
        user.UserDataLastModifiedAt = user.UserPasswordsDataLastModifiedAt;
        user.LastModifiedAt = user.UserPasswordsDataLastModifiedAt;
        user.GenerateIntegrityHash();
        database.Users.Update(user);
        var second = await service.GetOrCreateAsync(user);

        database.Db.ChangeTracker.Clear();
        var snapshots = await database.Db.UserSyncSnapshots
            .Where(snapshot => snapshot.UserId == user.UId)
            .ToListAsync();
        var state = await database.UserSyncStates.GetAsync(user.UId);
        var knowledge = await database.UserRevisionKnowledge.GetAsync(
            user.UId,
            identity.LocalDeviceId,
            identity.OriginInstanceId,
            user.KeyEpoch);

        MSTestAssert.AreEqual(1L, firstRevision);
        MSTestAssert.IsNotNull(firstEnvelope);
        var selfCoverage = firstEnvelope.Coverage.Single(entry =>
            entry.OriginDeviceId == identity.LocalDeviceId &&
            entry.OriginInstanceId == identity.OriginInstanceId &&
            entry.UserKeyEpoch == user.KeyEpoch);
        MSTestAssert.AreEqual(1L, selfCoverage.OriginRevision);
        MSTestAssert.AreEqual(firstId, unchangedId);
        MSTestAssert.AreEqual(1L, unchangedRevision);
        MSTestAssert.AreEqual(2L, second.OriginRevision);
        MSTestAssert.HasCount(1, snapshots);
        MSTestAssert.AreEqual(2L, snapshots[0].OriginRevision);
        MSTestAssert.IsNotNull(state);
        MSTestAssert.AreEqual(3L, state.NextOriginRevision);
        MSTestAssert.IsNotNull(knowledge);
        MSTestAssert.AreEqual(2L, knowledge.HighestStoredRevision);
        MSTestAssert.AreEqual(2L, knowledge.HighestMergedRevision);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task GetOrCreateAsync_NewInstallationInstance_RestartsRevisionInSeparateOriginNamespace()
    {
        await using var database = await SqliteIntegrationTestDatabase.CreateAsync();
        var user = CreateUser();
        await database.Users.AddAsync(user);
        await database.UnitOfWork.SaveChangesAsync();
        using var signingKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters());
        var identity = CreateIdentity(signingKey);
        var service = new UserSnapshotPublisherService(
            database.UserSyncSnapshots,
            database.UserSyncStates,
            database.UserRevisionKnowledge,
            identity,
            database.UnitOfWork,
            new UserLifecycleCoordinator());

        var first = await service.GetOrCreateAsync(user);
        var oldInstance = identity.OriginInstanceId;
        identity.OriginInstanceId = Guid.NewGuid();
        var afterReset = await service.GetOrCreateAsync(user);

        var snapshots = await database.Db.UserSyncSnapshots
            .Where(snapshot => snapshot.UserId == user.UId)
            .OrderBy(snapshot => snapshot.OriginInstanceId)
            .ToListAsync();

        MSTestAssert.AreEqual(1L, first.OriginRevision);
        MSTestAssert.AreEqual(1L, afterReset.OriginRevision);
        MSTestAssert.AreNotEqual(oldInstance, afterReset.OriginInstanceId);
        MSTestAssert.HasCount(2, snapshots);
        MSTestAssert.AreEqual(2, snapshots.Select(snapshot => snapshot.OriginInstanceId).Distinct().Count());
    }

    private static User CreateUser()
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
        return user;
    }

    private static FakeDeviceIdentityService CreateIdentity(Key key) =>
        new()
        {
            LocalDeviceId = Guid.NewGuid(),
            OriginInstanceId = Guid.NewGuid(),
            SignPublicKey = key.PublicKey.Export(KeyBlobFormat.RawPublicKey),
            SignHandler = data => SignatureAlgorithm.Ed25519.Sign(key, data)
        };
}
