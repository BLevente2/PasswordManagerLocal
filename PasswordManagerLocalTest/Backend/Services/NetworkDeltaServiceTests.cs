using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocalBackend.Abstractions.Persistence;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Security;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Services;
using PasswordManagerLocalBackend.Sync;
using PasswordManagerLocalTest.Fakes;
using PasswordManagerLocalTest.TestInfrastructure;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using PasswordManagerLocalBackend.Abstractions.Providers;

namespace PasswordManagerLocalTest.Backend.Services;

[TestClass]
public sealed class NetworkDeltaServiceTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task Apply_WhenLocalSyncIsDisabled_RejectsBeforeProcessingEnvelope()
    {
        var identity = new FakeDeviceIdentityService { IsSyncOn = false };
        var service = CreateService(identity, new FakeDeviceRepository());

        await ExpectThrowsAsync<SyncRouteDisabledException>(() => service.ApplyAsync(new NetworkDelta()));
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task Apply_WhenDeltaTargetsAnotherDevice_RejectsIt()
    {
        using var setup = await CreateValidDeltaAsync();
        setup.Delta.RecipientDeviceId = Guid.NewGuid().ToString("N");
        var service = CreateService(setup.Recipient, new FakeDeviceRepository());

        await ExpectThrowsAsync<UnauthorizedAccessException>(() => service.ApplyAsync(setup.Delta));
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task Apply_WhenSignatureIsTampered_RejectsBeforeDatabaseLookup()
    {
        using var setup = await CreateValidDeltaAsync();
        setup.Delta.Sig[0] ^= 0xFF;
        var service = CreateService(setup.Recipient, new FakeDeviceRepository());

        await ExpectThrowsAsync<InvalidDataException>(() => service.ApplyAsync(setup.Delta));
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task Apply_WhenSignedSourceDeviceIsUnknown_RejectsIt()
    {
        using var setup = await CreateValidDeltaAsync();
        var service = CreateService(setup.Recipient, new FakeDeviceRepository());

        await ExpectThrowsAsync<UnauthorizedAccessException>(() => service.ApplyAsync(setup.Delta));
    }

    private static NetworkDeltaService CreateService(IDeviceIdentityService identity, FakeDeviceRepository devices) =>
        new(
            new FakeOutgoingDeltaBuilderService(),
            new InMemoryUserRepository(),
            new FakeGroupRepository(),
            devices,
            new FakeUserDeviceRepository(),
            new FakeLocalUserDeviceRepository(),
            new FakeSyncTombstoneRepository(),
            new FakeSyncQueueRepository(),
            new FakeSyncQueueService(),
            new FakeSyncDeviceIdentityService(),
            identity,
            new FakeSyncAuthorizationService(),
            new FakeSyncRuntimeService(),
            new FakeAuthService(),
            new TestKeyProtector(),
            new FakeUnitOfWork());

    private static async Task<ValidDeltaSetup> CreateValidDeltaAsync()
    {
        var senderProvider = CreateIdentityProvider();
        var recipientProvider = CreateIdentityProvider();
        var sender = CreateIdentity(senderProvider);
        var recipient = CreateIdentity(recipientProvider);
        await sender.InitializeAsync();
        await sender.SetSyncOnAsync(true);
        await recipient.InitializeAsync();
        await recipient.SetSyncOnAsync(true);

        var target = new Device
        {
            Id = recipient.LocalDeviceId,
            PublicKey = recipient.AgreementPublicKey,
            SignPublicKey = recipient.SignPublicKey,
            TlsCertFingerprint = recipient.FingerprintHex,
            DeviceType = recipient.DeviceType,
            IsTrusted = true
        };
        var builder = new OutgoingDeltaBuilderService(
            new InMemoryUserRepository(),
            new FakeGroupRepository(),
            new FakeDeviceRepository(),
            new FakeUserDeviceRepository(),
            new FakeLocalUserDeviceRepository(),
            sender);
        var delta = await builder.BuildAsync(new SyncItem
        {
            ModelId = Guid.NewGuid(),
            ModelType = SyncModelType.Group,
            ChangeType = SyncChangeType.Deleted
        }, target);

        return new ValidDeltaSetup(delta, recipient, senderProvider, recipientProvider);
    }

    private static ServiceProvider CreateIdentityProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceIdentityRepository, FakeDeviceIdentityRepository>();
        services.AddSingleton<IUnitOfWork, FakeUnitOfWork>();
        services.AddSingleton<IKeyProtector, TestKeyProtector>();
        services.AddSingleton<ILocalDeviceTypeProvider, FakeLocalDeviceTypeProvider>();
        return services.BuildServiceProvider();
    }

    private static DeviceIdentityService CreateIdentity(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ILocalDeviceTypeProvider>());

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

    private sealed record ValidDeltaSetup(
        NetworkDelta Delta,
        DeviceIdentityService Recipient,
        ServiceProvider SenderProvider,
        ServiceProvider RecipientProvider) : IDisposable
    {
        public void Dispose()
        {
            SenderProvider.Dispose();
            RecipientProvider.Dispose();
        }
    }
}
