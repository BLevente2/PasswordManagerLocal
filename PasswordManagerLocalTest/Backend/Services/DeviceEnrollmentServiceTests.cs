using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocalBackend.Responses;
using PasswordManagerLocalBackend.Services;
using PasswordManagerLocalTest.Fakes;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using PasswordManagerLocalBackend.Caching;

namespace PasswordManagerLocalTest.Backend.Services;

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
        var service = new DeviceEnrollmentService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FakeDeviceIdentityService(),
            new DiscoveredDeviceEndpointCache(),
            new FakeSyncTransportClientService(),
            runtime);
        return new EnrollmentServiceSetup(service, provider, runtime);
    }

    private sealed record EnrollmentServiceSetup(
        DeviceEnrollmentService Service,
        ServiceProvider Provider,
        FakeSyncRuntimeService Runtime) : IDisposable
    {
        public void Dispose()
        {
            Service.Dispose();
            Provider.Dispose();
        }
    }
}
