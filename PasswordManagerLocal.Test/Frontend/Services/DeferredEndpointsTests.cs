using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Backend.Hosting;
using PasswordManagerLocal.Frontend.Services;
using PasswordManagerLocal.Test.Fakes;
using PasswordManagerLocal.Test.TestInfrastructure;

namespace PasswordManagerLocal.Test.Frontend.Services;

[TestClass]
public sealed class DeferredEndpointsTests
{
    [TestMethod]
    public async Task EachOperation_UsesTheActiveInteractiveSession()
    {
        using var host = new BackendTestHost();
        var runtime = new FakeBackendRuntime(host.Services.GetRequiredService<IEndpoints>());
        var lifetimeCoordinator = new BackendRuntimeLifetimeCoordinator(runtime);
        await using var backendClient = new InProcessFrontendBackendClient(runtime, lifetimeCoordinator);
        await backendClient.ConnectAsync();
        var endpoints = new DeferredEndpoints(backendClient);
        var token = Guid.NewGuid();

        _ = await endpoints.GetAuthSessionStatusAsync(token);
        _ = await endpoints.GetAuthSessionStatusAsync(token);

        Assert.AreEqual(1, runtime.OpenInteractiveSessionCalls);
    }
}
