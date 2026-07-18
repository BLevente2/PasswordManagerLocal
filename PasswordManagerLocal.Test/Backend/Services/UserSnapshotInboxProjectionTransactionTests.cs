using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSec.Cryptography;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using PasswordManagerLocal.Test.Fakes;
using PasswordManagerLocal.Test.TestInfrastructure;
using System.Security.Cryptography;
using System.Text;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.Backend.Services;

[TestClass]
public sealed class UserSnapshotInboxProjectionTransactionTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    [TestCategory("Security")]
    public async Task ProjectionPersistenceFailure_RollsBackPendingSnapshotAndKeepsCanonicalIdentity()
    {
        await using var database = await SqliteIntegrationTestDatabase.CreateAsync();
        var lifecycle = new UserLifecycleCoordinator();
        var membership = new FakeUserMembershipAuthorizationService();
        var canonicalProjection = new UserLoginIdentityProjectionService(
            database.Users,
            database.UserSyncSnapshots,
            membership,
            lifecycle,
            database.UnitOfWork,
            database.DeletedUserBarriers);
        var canonicalVersion = Stamp(1_000, Guid.NewGuid(), Guid.NewGuid());
        var user = await AddCanonicalUserAsync(database, canonicalProjection, "OldName", canonicalVersion);
        var throwingProjection = new ThrowingProjectionService(canonicalProjection);
        var clock = new RecordingVersionClock();
        var localIdentity = new FakeDeviceIdentityService
        {
            LocalDeviceId = Guid.NewGuid(),
            OriginInstanceId = Guid.NewGuid()
        };
        var inbox = new UserSnapshotInboxService(
            database.Users,
            database.UserSyncSnapshots,
            database.UserRevisionKnowledge,
            localIdentity,
            database.UnitOfWork,
            lifecycle,
            membership,
            database.DeletedUserBarriers,
            throwingProjection,
            clock);
        using var signingKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters());
        var originDevice = Guid.NewGuid();
        var originInstance = Guid.NewGuid();
        var envelope = CreateEnvelope(
            user,
            "NewName",
            Stamp(2_000, originDevice, originInstance),
            originDevice,
            originInstance,
            signingKey);

        await MSTestAssert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            inbox.StoreAsync(envelope, Guid.NewGuid()));

        database.Db.ChangeTracker.Clear();
        MSTestAssert.IsNull(await database.UserSyncSnapshots.GetAsync(
            user.UId,
            originDevice,
            originInstance,
            user.KeyEpoch));
        MSTestAssert.IsNull(await database.UserRevisionKnowledge.GetAsync(
            user.UId,
            originDevice,
            originInstance,
            user.KeyEpoch));
        MSTestAssert.AreEqual(UserLoginIdentityMatchState.Matched,
            (await canonicalProjection.FindByUsernameAsync(Encoding.UTF8.GetBytes("OldName"))).State);
        MSTestAssert.AreEqual(UserLoginIdentityMatchState.NotFound,
            (await canonicalProjection.FindByUsernameAsync(Encoding.UTF8.GetBytes("NewName"))).State);
        MSTestAssert.AreEqual(0, clock.Observed.Count);
    }


    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task AcceptedPendingUsername_AdvancesDeterministicClockAfterAtomicCommit()
    {
        await using var database = await SqliteIntegrationTestDatabase.CreateAsync();
        var lifecycle = new UserLifecycleCoordinator();
        var membership = new FakeUserMembershipAuthorizationService();
        var projection = new UserLoginIdentityProjectionService(
            database.Users,
            database.UserSyncSnapshots,
            membership,
            lifecycle,
            database.UnitOfWork,
            database.DeletedUserBarriers);
        var canonicalVersion = Stamp(1_000, Guid.NewGuid(), Guid.NewGuid());
        var user = await AddCanonicalUserAsync(database, projection, "OldName", canonicalVersion);
        var localIdentity = new FakeDeviceIdentityService
        {
            LocalDeviceId = Guid.NewGuid(),
            OriginInstanceId = Guid.NewGuid()
        };
        var clock = new RecordingVersionClock();
        var inbox = new UserSnapshotInboxService(
            database.Users,
            database.UserSyncSnapshots,
            database.UserRevisionKnowledge,
            localIdentity,
            database.UnitOfWork,
            lifecycle,
            membership,
            database.DeletedUserBarriers,
            projection,
            clock);
        using var signingKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters());
        var originDevice = Guid.NewGuid();
        var originInstance = Guid.NewGuid();
        var incomingVersion = Stamp(2_000, originDevice, originInstance);
        var envelope = CreateEnvelope(user, "NewName", incomingVersion, originDevice, originInstance, signingKey);

        var receipt = await inbox.StoreAsync(envelope, Guid.NewGuid());

        MSTestAssert.AreEqual(UserSnapshotReceiptState.StoredPending, receipt.State);
        MSTestAssert.AreEqual(1, clock.Observed.Count);
        MSTestAssert.AreEqual(incomingVersion, clock.Observed[0]);
        MSTestAssert.AreEqual(UserLoginIdentityMatchState.NotFound,
            (await projection.FindByUsernameAsync(Encoding.UTF8.GetBytes("OldName"))).State);
        MSTestAssert.AreEqual(UserLoginIdentityMatchState.Matched,
            (await projection.FindByUsernameAsync(Encoding.UTF8.GetBytes("NewName"))).State);
    }

    private static async Task<User> AddCanonicalUserAsync(
        SqliteIntegrationTestDatabase database,
        IUserLoginIdentityProjectionService projection,
        string username,
        SyncVersionStamp version)
    {
        var bytes = Encoding.UTF8.GetBytes(username);
        var salt = Hashing.GenerateSalt();
        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            UId = Guid.NewGuid(),
            UsernameHash = Hashing.SHA256Hash(bytes, salt),
            UsernameSalt = salt,
            PasswordSalt = Enumerable.Repeat((byte)0x03, 32).ToArray(),
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
        user.SetGeneralUserDataVersion(version);
        user.GenerateIntegrityHash();
        await database.Users.AddAsync(user);
        await database.UnitOfWork.SaveChangesAsync();
        await projection.SetCanonicalAsync(user, version);
        await database.UnitOfWork.SaveChangesAsync();
        CryptographicOperations.ZeroMemory(bytes);
        return user;
    }

    private static UserSnapshotEnvelope CreateEnvelope(
        User user,
        string username,
        SyncVersionStamp version,
        Guid originDevice,
        Guid originInstance,
        Key key)
    {
        var bytes = Encoding.UTF8.GetBytes(username);
        var salt = Hashing.GenerateSalt();
        var createdAt = DateTimeOffset.UtcNow;
        var payload = new UserSyncPayload
        {
            UId = user.UId,
            UsernameHash = Hashing.SHA256Hash(bytes, salt),
            UsernameSalt = salt,
            GeneralUserDataVersion = version,
            PasswordSalt = user.PasswordSalt.ToArray(),
            EncryptedPayload = [0x11],
            EncryptedGeneralUserDataPayload = [0x21],
            EncryptedUserPasswordsDataPayload = [0x31],
            EncryptedUserDevicesDataPayload = [0x41],
            UserDataLastModifiedAt = createdAt,
            GeneralUserDataLastModifiedAt = createdAt,
            UserPasswordsDataLastModifiedAt = createdAt,
            UserDevicesDataLastModifiedAt = createdAt,
            DeviceIds = [originDevice]
        };
        payload.IntegrityHash = SyncCryptoUtil.CalculateUserHash(payload, createdAt.ToUnixTimeMilliseconds());
        var envelope = new UserSnapshotEnvelope
        {
            UserId = user.UId,
            OriginDeviceId = originDevice,
            OriginInstanceId = originInstance,
            OriginRevision = 1,
            UserKeyEpoch = user.KeyEpoch,
            MembershipEpoch = user.MembershipEpoch,
            CreatedAtUtc = createdAt,
            User = payload
        };
        UserSnapshotEnvelopeUtil.FillOriginAuthentication(envelope, new FakeDeviceIdentityService
        {
            LocalDeviceId = originDevice,
            OriginInstanceId = originInstance,
            SignPublicKey = key.PublicKey.Export(KeyBlobFormat.RawPublicKey),
            SignHandler = data => SignatureAlgorithm.Ed25519.Sign(key, data)
        });
        CryptographicOperations.ZeroMemory(bytes);
        return envelope;
    }

    private static SyncVersionStamp Stamp(long physical, Guid device, Guid instance) => new()
    {
        PhysicalTimeUnixMilliseconds = physical,
        LogicalCounter = 0,
        OriginDeviceId = device,
        OriginInstanceId = instance
    };


    private sealed class RecordingVersionClock : ISyncVersionClockService
    {
        public List<SyncVersionStamp> Observed { get; } = [];

        public SyncVersionStamp Next() => throw new NotSupportedException();

        public void Observe(IEnumerable<SyncVersionStamp> stamps) => Observed.AddRange(stamps);
    }

    private sealed class ThrowingProjectionService : IUserLoginIdentityProjectionService
    {
        private readonly IUserLoginIdentityProjectionService _inner;

        public ThrowingProjectionService(IUserLoginIdentityProjectionService inner) => _inner = inner;

        public Task<UserLoginIdentityState> SetCanonicalAsync(User user, SyncVersionStamp generalUserDataVersion, CancellationToken ct = default) =>
            _inner.SetCanonicalAsync(user, generalUserDataVersion, ct);

        public Task<UserLoginIdentityState?> RecalculateAsync(Guid userId, CancellationToken ct = default) =>
            throw new InvalidOperationException("Injected projection persistence failure.");

        public Task<UserLoginIdentityState?> RecalculateUnderLifecycleAsync(Guid userId, CancellationToken ct = default) =>
            throw new InvalidOperationException("Injected projection persistence failure.");

        public Task<UserLoginIdentityMatchResult> FindByUsernameAsync(byte[] normalizedUsername, CancellationToken ct = default) =>
            _inner.FindByUsernameAsync(normalizedUsername, ct);
    }
}
