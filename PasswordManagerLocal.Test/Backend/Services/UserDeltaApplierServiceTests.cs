using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Test.Fakes;
using PasswordManagerLocal.Test.TestInfrastructure;

namespace PasswordManagerLocal.Test.Backend.Services;

[TestClass]
public sealed class UserDeltaApplierServiceTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task Apply_WhenConcurrentUserBundlesNeedMergeButEncryptionKeyIsUnavailable_DefersWithoutChangingStoredUser()
    {
        var users = new InMemoryUserRepository();
        var groups = new FakeGroupRepository();
        var devices = new FakeDeviceRepository();
        var userDevices = new FakeUserDeviceRepository();
        var identity = new FakeDeviceIdentityService();
        var relationships = new SyncRelationshipReconciliationService(users, groups, devices, userDevices, identity);
        var bundleSync = new UserDataBundleSyncService(
            users,
            new FakeAuthService(),
            new TestKeyProtector(),
            new UserDataBundleIntegrityService(),
            new UserPasswordsDataMergeService(),
            new UserDevicesDataMergeService(),
            relationships);
        var service = new UserDeltaApplierService(
            users,
            new FakeSyncTombstoneRepository(),
            new FakeSyncQueueService(),
            bundleSync,
            relationships);

        var userId = Guid.NewGuid();
        var passwordSalt = new byte[] { 1, 2, 3, 4 };
        var baseline = DateTimeOffset.UtcNow.AddMinutes(-10);
        var existing = new User
        {
            UId = userId,
            PasswordSalt = passwordSalt,
            EncryptedGeneralUserDataPayload = new byte[] { 10 },
            EncryptedUserPasswordsDataPayload = new byte[] { 20 },
            EncryptedUserDevicesDataPayload = new byte[] { 30 },
            LastModifiedAt = baseline,
            GeneralUserDataLastModifiedAt = baseline.AddMinutes(3),
            UserPasswordsDataLastModifiedAt = baseline.AddMinutes(1),
            UserDevicesDataLastModifiedAt = baseline.AddMinutes(1)
        };
        await users.AddAsync(existing);

        var incoming = new UserSyncPayload
        {
            UId = userId,
            PasswordSalt = passwordSalt.ToArray(),
            EncryptedGeneralUserDataPayload = new byte[] { 11 },
            EncryptedUserPasswordsDataPayload = new byte[] { 21 },
            EncryptedUserDevicesDataPayload = new byte[] { 31 },
            UserDataLastModifiedAt = baseline.AddMinutes(4),
            GeneralUserDataLastModifiedAt = baseline.AddMinutes(1),
            UserPasswordsDataLastModifiedAt = baseline.AddMinutes(4),
            UserDevicesDataLastModifiedAt = baseline.AddMinutes(1)
        };
        var delta = new SyncDeltaPayload
        {
            ModelId = userId,
            ModelType = SyncModelType.User,
            ChangeType = SyncChangeType.Updated,
            User = incoming
        };
        var deltaTimestamp = baseline.AddMinutes(4).ToUnixTimeMilliseconds();

        await ExpectThrowsAsync<SyncDeltaDeferredException>(
            () => service.ApplyAsync(delta, Guid.NewGuid(), deltaTimestamp, CancellationToken.None));

        var stored = await users.GetByIdAsync(userId);
        Assert.IsNotNull(stored);
        CollectionAssert.AreEqual(existing.EncryptedGeneralUserDataPayload, stored.EncryptedGeneralUserDataPayload);
        CollectionAssert.AreEqual(existing.EncryptedUserPasswordsDataPayload, stored.EncryptedUserPasswordsDataPayload);
        CollectionAssert.AreEqual(existing.EncryptedUserDevicesDataPayload, stored.EncryptedUserDevicesDataPayload);
        Assert.AreEqual(existing.GeneralUserDataLastModifiedAt, stored.GeneralUserDataLastModifiedAt);
        Assert.AreEqual(existing.UserPasswordsDataLastModifiedAt, stored.UserPasswordsDataLastModifiedAt);
        Assert.AreEqual(existing.UserDevicesDataLastModifiedAt, stored.UserDevicesDataLastModifiedAt);
    }

    private static async Task ExpectThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action();
            Assert.Fail($"Expected exception: {typeof(TException).Name}");
        }
        catch (TException)
        {
        }
    }
}
