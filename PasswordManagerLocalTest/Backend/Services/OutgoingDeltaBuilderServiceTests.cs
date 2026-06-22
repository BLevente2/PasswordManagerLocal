using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocalBackend.Abstractions.Persistence;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Security;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Security;
using PasswordManagerLocalBackend.Services;
using PasswordManagerLocalBackend.Sync;
using PasswordManagerLocalTest.Fakes;
using PasswordManagerLocalTest.TestInfrastructure;
using System.Text.Json;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocalTest.Backend.Services;

[TestClass]
public sealed class OutgoingDeltaBuilderServiceTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task BuildUserDelta_ProducesSignedEncryptedPayloadThatRecipientCanValidate()
    {
        using var senderProvider = CreateIdentityProvider();
        using var recipientProvider = CreateIdentityProvider();
        var sender = CreateIdentity(senderProvider);
        var recipient = CreateIdentity(recipientProvider);
        await sender.InitializeAsync();
        await sender.SetSyncOnAsync(true);
        await recipient.InitializeAsync();
        var target = CreateTargetDevice(recipient);
        var users = new InMemoryUserRepository();
        var user = new User
        {
            UId = Guid.NewGuid(),
            UsernameHash = [1, 2],
            UsernameSalt = [3, 4],
            PasswordSalt = [5, 6],
            EncryptedPayload = [7, 8, 9],
            SavedKey = [99],
            LastModifiedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        var link = new UserDevice
        {
            UserId = user.UId,
            DeviceId = target.Id,
            Device = target,
            IsSyncOn = true,
            IsDeleted = false
        };
        link.GenerateIntegrityHash();
        user.UserDevices.Add(link);
        user.GenerateIntegrityHash();
        await users.AddAsync(user);
        var builder = new OutgoingDeltaBuilderService(
            users,
            new FakeGroupRepository(),
            new FakeDeviceRepository(),
            new FakeUserDeviceRepository(),
            new FakeLocalUserDeviceRepository(),
            sender);
        var changedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var delta = await builder.BuildAsync(new SyncItem
        {
            ModelId = user.UId,
            ModelType = SyncModelType.User,
            ChangeType = SyncChangeType.Updated,
            ChangedAtTs = changedAt
        }, target);

        MSTestAssert.AreEqual(target.Id.ToString("N"), delta.RecipientDeviceId);
        MSTestAssert.AreEqual(sender.DeviceIdHex, delta.DeviceId);
        MSTestAssert.AreEqual(changedAt, delta.Ts);
        MSTestAssert.IsTrue(NetDeltaSigner.VerifySignature(delta));
        var plaintext = recipient.DecryptFromDevice(
            delta.Payload,
            delta.EphemeralPublicKey,
            delta.Nonce,
            delta.Tag,
            SyncCryptoUtil.BuildAssociatedData(delta));
        CollectionAssert.AreEqual(Hashing.SHA512Hash(plaintext), delta.PayloadHash);
        var payload = JsonSerializer.Deserialize<SyncDeltaPayload>(plaintext);
        MSTestAssert.IsNotNull(payload);
        SyncCryptoUtil.ValidatePayloadIntegrity(payload, delta.Ts);
        MSTestAssert.AreEqual(user.UId, payload.ModelId);
        MSTestAssert.AreEqual(SyncModelType.User, payload.ModelType);
        MSTestAssert.AreEqual(SyncChangeType.Updated, payload.ChangeType);
        MSTestAssert.IsNotNull(payload.User);
        CollectionAssert.AreEqual(user.EncryptedPayload, payload.User.EncryptedPayload);
        MSTestAssert.IsTrue(payload.User.DeviceIds.Contains(sender.LocalDeviceId));
        MSTestAssert.IsTrue(payload.User.DeviceIds.Contains(target.Id));
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task BuildDeletedDelta_DoesNotRequireDeletedModelToStillExist()
    {
        using var senderProvider = CreateIdentityProvider();
        using var recipientProvider = CreateIdentityProvider();
        var sender = CreateIdentity(senderProvider);
        var recipient = CreateIdentity(recipientProvider);
        await sender.InitializeAsync();
        await sender.SetSyncOnAsync(true);
        await recipient.InitializeAsync();
        var target = CreateTargetDevice(recipient);
        var builder = new OutgoingDeltaBuilderService(
            new InMemoryUserRepository(),
            new FakeGroupRepository(),
            new FakeDeviceRepository(),
            new FakeUserDeviceRepository(),
            new FakeLocalUserDeviceRepository(),
            sender);
        var deletedId = Guid.NewGuid();

        var delta = await builder.BuildAsync(new SyncItem
        {
            ModelId = deletedId,
            ModelType = SyncModelType.Group,
            ChangeType = SyncChangeType.Deleted
        }, target);

        var plaintext = recipient.DecryptFromDevice(
            delta.Payload,
            delta.EphemeralPublicKey,
            delta.Nonce,
            delta.Tag,
            SyncCryptoUtil.BuildAssociatedData(delta));
        var payload = JsonSerializer.Deserialize<SyncDeltaPayload>(plaintext);
        MSTestAssert.IsNotNull(payload);
        MSTestAssert.AreEqual(deletedId, payload.ModelId);
        MSTestAssert.AreEqual(SyncModelType.Group, payload.ModelType);
        MSTestAssert.AreEqual(SyncChangeType.Deleted, payload.ChangeType);
        MSTestAssert.IsNull(payload.Group);
        SyncCryptoUtil.ValidatePayloadIntegrity(payload, delta.Ts);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task Build_RejectsBlockedUntrustedAndLocalTargets()
    {
        using var senderProvider = CreateIdentityProvider();
        using var recipientProvider = CreateIdentityProvider();
        var sender = CreateIdentity(senderProvider);
        var recipient = CreateIdentity(recipientProvider);
        await sender.InitializeAsync();
        await sender.SetSyncOnAsync(true);
        await recipient.InitializeAsync();
        var builder = new OutgoingDeltaBuilderService(
            new InMemoryUserRepository(),
            new FakeGroupRepository(),
            new FakeDeviceRepository(),
            new FakeUserDeviceRepository(),
            new FakeLocalUserDeviceRepository(),
            sender);
        var item = new SyncItem
        {
            ModelId = Guid.NewGuid(),
            ModelType = SyncModelType.User,
            ChangeType = SyncChangeType.Deleted
        };
        var blocked = CreateTargetDevice(recipient);
        blocked.IsBlocked = true;
        var untrusted = CreateTargetDevice(recipient);
        untrusted.IsTrusted = false;
        var local = new Device
        {
            Id = sender.LocalDeviceId,
            PublicKey = sender.AgreementPublicKey,
            SignPublicKey = sender.SignPublicKey,
            TlsCertFingerprint = sender.FingerprintHex,
            IsTrusted = true
        };

        await ExpectThrowsAsync<InvalidOperationException>(() => builder.BuildAsync(item, blocked));
        await ExpectThrowsAsync<InvalidOperationException>(() => builder.BuildAsync(item, untrusted));
        await ExpectThrowsAsync<InvalidOperationException>(() => builder.BuildAsync(item, local));
    }

    private static ServiceProvider CreateIdentityProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceIdentityRepository, FakeDeviceIdentityRepository>();
        services.AddSingleton<IUnitOfWork, FakeUnitOfWork>();
        services.AddSingleton<IKeyProtector, TestKeyProtector>();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    private static DeviceIdentityService CreateIdentity(IServiceProvider provider) =>
        new(provider.GetRequiredService<IServiceScopeFactory>());

    private static Device CreateTargetDevice(DeviceIdentityService recipient) =>
        new()
        {
            Id = recipient.LocalDeviceId,
            PublicKey = recipient.AgreementPublicKey,
            SignPublicKey = recipient.SignPublicKey,
            TlsCertFingerprint = recipient.FingerprintHex,
            DeviceType = recipient.DeviceType,
            IsTrusted = true,
            IsBlocked = false
        };

    private static async Task ExpectThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action();
            MSTestAssert.Fail($"Expected exception: {typeof(TException).Name}");
        }
        catch (TException)
        {
        }
    }
}
