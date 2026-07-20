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
    public async Task EachOperation_RequestsCurrentEndpointsFromRuntime()
    {
        using var host = new BackendTestHost();
        var runtime = new FakeBackendRuntime(host.Services.GetRequiredService<IEndpoints>());
        await using var backendClient = new InProcessFrontendBackendClient(runtime);
        var endpoints = new DeferredEndpoints(backendClient);
        var token = Guid.NewGuid();

        _ = await endpoints.GetAuthSessionStatusAsync(token);
        _ = await endpoints.GetAuthSessionStatusAsync(token);

        Assert.AreEqual(2, runtime.GetEndpointsCalls);
    }
}
