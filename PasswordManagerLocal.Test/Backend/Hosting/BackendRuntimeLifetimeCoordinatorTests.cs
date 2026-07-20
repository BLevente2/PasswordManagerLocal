using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Backend.Hosting;
using PasswordManagerLocal.Runtime.Abstractions;
using PasswordManagerLocal.Test.Fakes;
using PasswordManagerLocal.Test.TestInfrastructure;

namespace PasswordManagerLocal.Test.Backend.Hosting;

[TestClass]
public sealed class BackendRuntimeLifetimeCoordinatorTests
{
    [TestMethod]
    public async Task FirstLeaseStartsAndFinalLeaseStopsRuntime()
    {
        using var host = new BackendTestHost();
        var runtime = new FakeBackendRuntime(host.Services.GetRequiredService<IEndpoints>());
        var coordinator = new BackendRuntimeLifetimeCoordinator(runtime);

        var interactive = await coordinator.AcquireAsync(BackendLifetimeReason.InteractiveUi);
        var background = await coordinator.AcquireAsync(BackendLifetimeReason.BackgroundSync);

        Assert.AreEqual(1, runtime.EnsureStartedCalls);
        Assert.AreEqual(
            BackendLifetimeReason.InteractiveUi | BackendLifetimeReason.BackgroundSync,
            coordinator.ActiveReasons);

        await interactive.DisposeAsync();
        Assert.AreEqual(0, runtime.StopCalls);
        Assert.AreEqual(BackendLifetimeReason.BackgroundSync, coordinator.ActiveReasons);

        await background.DisposeAsync();
        Assert.AreEqual(1, runtime.StopCalls);
        Assert.AreEqual(BackendLifetimeReason.None, coordinator.ActiveReasons);
    }

    [TestMethod]
    public async Task DuplicateReasonLeasesAreIndependentAndDuplicateDisposalIsSafe()
    {
        using var host = new BackendTestHost();
        var runtime = new FakeBackendRuntime(host.Services.GetRequiredService<IEndpoints>());
        var coordinator = new BackendRuntimeLifetimeCoordinator(runtime);

        var first = await coordinator.AcquireAsync(BackendLifetimeReason.InteractiveUi);
        var second = await coordinator.AcquireAsync(BackendLifetimeReason.InteractiveUi);

        await first.DisposeAsync();
        await first.DisposeAsync();
        Assert.AreEqual(0, runtime.StopCalls);
        Assert.AreEqual(BackendLifetimeReason.InteractiveUi, coordinator.ActiveReasons);

        await second.DisposeAsync();
        Assert.AreEqual(1, runtime.StopCalls);
    }

    [TestMethod]
    public async Task StartupFailureRollsBackReason()
    {
        using var host = new BackendTestHost();
        var runtime = new FakeBackendRuntime(host.Services.GetRequiredService<IEndpoints>())
        {
            StartupFailure = new InvalidOperationException("startup failed")
        };
        var coordinator = new BackendRuntimeLifetimeCoordinator(runtime);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await coordinator.AcquireAsync(BackendLifetimeReason.BackgroundSync));

        Assert.AreEqual(BackendLifetimeReason.None, coordinator.ActiveReasons);
        Assert.AreEqual(0, runtime.StopCalls);
    }

    [TestMethod]
    public async Task CancellationAfterStartupDoesNotRecordAReasonAndStopsRuntime()
    {
        using var host = new BackendTestHost();
        using var cancellation = new CancellationTokenSource();
        var runtime = new FakeBackendRuntime(host.Services.GetRequiredService<IEndpoints>())
        {
            AfterStart = cancellation.Cancel
        };
        var coordinator = new BackendRuntimeLifetimeCoordinator(runtime);

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await coordinator.AcquireAsync(
                BackendLifetimeReason.BackgroundSync,
                cancellation.Token));

        Assert.AreEqual(BackendLifetimeReason.None, coordinator.ActiveReasons);
        Assert.AreEqual(1, runtime.StopCalls);
    }

    [TestMethod]
    public async Task ConcurrentAcquisitionAndReleaseAreSerialized()
    {
        using var host = new BackendTestHost();
        var runtime = new FakeBackendRuntime(host.Services.GetRequiredService<IEndpoints>());
        var coordinator = new BackendRuntimeLifetimeCoordinator(runtime);

        var leases = await Task.WhenAll(
            Enumerable.Range(0, 16)
                .Select(_ => coordinator.AcquireAsync(BackendLifetimeReason.BackgroundSync)));

        Assert.AreEqual(1, runtime.EnsureStartedCalls);
        await Task.WhenAll(leases.Select(lease => lease.DisposeAsync().AsTask()));
        Assert.AreEqual(1, runtime.StopCalls);
        Assert.AreEqual(BackendLifetimeReason.None, coordinator.ActiveReasons);
    }
}
