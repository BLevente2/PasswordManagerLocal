using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Responses;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Test.Fakes;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using PasswordManagerLocal.Backend.Sync.Discovery;

using PasswordManagerLocal.Test.TestInfrastructure.Services.Fixtures;
namespace PasswordManagerLocal.Test.Backend.Services;

[TestClass]
public sealed class DeviceEnrollmentServiceTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task GetEnrollmentStatus_WithoutSession_ReturnsNone()
    {
        using var setup = CreateService();

        var status = await setup.Service.GetEnrollmentStatusAsync();

        MSTestAssert.AreEqual(DeviceEnrollmentState.None, status.State);
        MSTestAssert.AreEqual(0, setup.Runtime.EndEnrollmentOnlyCalls);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task CancelEnrollment_EndsTemporaryRuntimeAndLeavesNoSession()
    {
        using var setup = CreateService();

        await setup.Service.CancelEnrollmentAsync();
        var status = await setup.Service.GetEnrollmentStatusAsync();

        MSTestAssert.AreEqual(1, setup.Runtime.EndEnrollmentOnlyCalls);
        MSTestAssert.AreEqual(DeviceEnrollmentState.None, status.State);
    }

    private static EnrollmentServiceSetup CreateService()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var runtime = new FakeSyncRuntimeService();
        var identity = new FakeDeviceIdentityService();
        var endpointRegistry = new DiscoveredDeviceEndpointRegistry();
        var transport = new FakeSyncTransportClientService();
        var networkAddresses = new FakeLocalNetworkAddressService();
        var snapshotService = new DeviceEnrollmentSnapshotService(identity);
        var localLinks = new DeviceEnrollmentLocalLinkService(identity);
        var service = new DeviceEnrollmentService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            identity,
            endpointRegistry,
            runtime,
            new FakeLocalDiscoveryService(),
            new DeviceEnrollmentEndpointService(identity, transport, networkAddresses),
            new DeviceEnrollmentRegistrationService(identity, endpointRegistry, networkAddresses, localLinks),
            snapshotService,
            new DeviceEnrollmentSnapshotTransferService(identity, transport, snapshotService),
            new DeviceEnrollmentSnapshotImporterService(identity, localLinks));
        return new EnrollmentServiceSetup(service, provider, runtime);
    }

}
