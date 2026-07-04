using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Hosting;
using PasswordManagerLocal.Test.Fakes;

namespace PasswordManagerLocal.Test.Backend.Hosting;

[TestClass]
public sealed class BackendServiceHostTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task StartAndStop_StartsInRegistrationOrderAndStopsInReverseOrder()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IBackendHostedService>(new FakeBackendHostedService("first", calls));
        services.AddSingleton<IBackendHostedService>(new FakeBackendHostedService("second", calls));

        await using var host = new BackendServiceHost(services.BuildServiceProvider());

        await host.StartAsync();
        await host.StopAsync();

        CollectionAssert.AreEqual(
            new[] { "start:first", "start:second", "stop:second", "stop:first" },
            calls);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task Start_WhenServiceFails_StopsServicesThatAlreadyStarted()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IBackendHostedService>(new FakeBackendHostedService("started", calls));
        services.AddSingleton<IBackendHostedService>(new FakeBackendHostedService("failing", calls, throwOnStart: true));

        await using var host = new BackendServiceHost(services.BuildServiceProvider());

        await ExpectThrowsAsync<InvalidOperationException>(() => host.StartAsync());

        CollectionAssert.AreEqual(
            new[] { "start:started", "start:failing", "stop:started" },
            calls);
    }

    private static async Task ExpectThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action();
            Assert.Fail($"Expected {typeof(TException).Name}.");
        }
        catch (TException)
        {
        }
    }
}
