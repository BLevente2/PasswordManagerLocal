using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Caching;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Test.Fakes;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.Backend.Services;

[TestClass]
public sealed class DeviceEnrollmentRegistrationServiceTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task QueueInitialSync_UsesDeferredQueueUntilEnrollmentTransactionCommits()
    {
        var userId = Guid.NewGuid();
        var newDeviceId = Guid.NewGuid();
        var queue = new FakeSyncQueueService();
        using var services = new ServiceCollection()
            .AddSingleton<IGroupRepository, FakeGroupRepository>()
            .AddSingleton<ISyncChangeQueueService>(queue)
            .BuildServiceProvider();
        var identity = new FakeDeviceIdentityService();
        var service = new DeviceEnrollmentRegistrationService(
            identity,
            new DiscoveredDeviceEndpointCache(),
            new FakeLocalNetworkAddressService(),
            new DeviceEnrollmentLocalLinkService(identity));

        await service.QueueInitialSyncAsync(services, userId, newDeviceId, CancellationToken.None);

        MSTestAssert.HasCount(3, queue.DeferredEnqueuedItems);
        MSTestAssert.IsTrue(queue.DeferredEnqueuedItems.Any(item =>
            item.ModelId == userId &&
            item.ModelType == SyncModelType.User &&
            item.ChangeType == SyncChangeType.Updated));
        MSTestAssert.IsTrue(queue.DeferredEnqueuedItems.Any(item =>
            item.ModelId == newDeviceId &&
            item.ModelType == SyncModelType.Device &&
            item.ChangeType == SyncChangeType.Created));
        MSTestAssert.IsTrue(queue.DeferredEnqueuedItems.Any(item =>
            item.ModelType == SyncModelType.UserDevice &&
            item.ChangeType == SyncChangeType.Created));
    }
}
