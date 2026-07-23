using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Runtime.Abstractions;
using PasswordManagerLocal.Windows.Agent.DatabaseReset;
using PasswordManagerLocal.Windows.Agent.Hosting;
using PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

namespace PasswordManagerLocal.Windows.Ipc.Test.Agent;

[TestClass]
public sealed class WindowsAgentDatabaseResetCoordinatorTests
{
    [TestMethod]
    public async Task ResetStopsEndpointBeforeRuntimeAndRestartsAcceptanceAfterward()
    {
        var operations = new List<string>();
        var endpoint = new FakeWindowsAgentEndpointHost { OperationLog = operations };
        var backend = new FakeWindowsAgentBackendRuntimeOwner
        {
            OperationLog = operations,
            Snapshot = CreateResettableSnapshot()
        };
        var coordinator = new WindowsAgentDatabaseResetCoordinator(
            endpoint,
            backend,
            new WindowsAgentShutdownCoordinator());

        var result = await coordinator.ResetAsync();

        Assert.IsTrue(result.Completed);
        CollectionAssert.AreEqual(
            new[] { "endpoint-stop", "backend-reset", "endpoint-start" },
            operations);
        Assert.IsFalse(coordinator.IsResetting);
    }

    [TestMethod]
    public async Task ConcurrentResetIsRejectedWhileFirstResetOwnsTheTransition()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var endpoint = new FakeWindowsAgentEndpointHost { StopRelease = release };
        var coordinator = new WindowsAgentDatabaseResetCoordinator(
            endpoint,
            new FakeWindowsAgentBackendRuntimeOwner { Snapshot = CreateResettableSnapshot() },
            new WindowsAgentShutdownCoordinator());

        var first = coordinator.ResetAsync();
        await WaitUntilAsync(() => coordinator.IsResetting);
        var second = await coordinator.ResetAsync();
        release.TrySetResult();
        var firstResult = await first;

        Assert.IsTrue(firstResult.Completed);
        Assert.IsFalse(second.Completed);
        Assert.IsFalse(second.RequiresProcessRestart);
        Assert.AreEqual("A database reset is already in progress.", second.SafeMessage);
    }

    [TestMethod]
    public async Task UiCancellationAfterEndpointShutdownDoesNotAbortAuthoritativeReset()
    {
        var stopRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var endpoint = new FakeWindowsAgentEndpointHost { StopRelease = stopRelease };
        var backend = new FakeWindowsAgentBackendRuntimeOwner
        {
            Snapshot = CreateResettableSnapshot()
        };
        var coordinator = new WindowsAgentDatabaseResetCoordinator(
            endpoint,
            backend,
            new WindowsAgentShutdownCoordinator());
        using var cancellation = new CancellationTokenSource();

        var reset = coordinator.ResetAsync(cancellation.Token);
        await WaitUntilAsync(() => endpoint.StopCount == 1);
        cancellation.Cancel();
        stopRelease.TrySetResult();
        var result = await reset;

        Assert.IsTrue(result.Completed);
        Assert.AreEqual(1, backend.ResetCount);
        Assert.AreEqual(1, endpoint.StartCount);
    }

    [TestMethod]
    public async Task FailureRequiresProcessRestartAndRequestsAgentShutdown()
    {
        var failure = new IOException("database deletion failed");
        var backend = new FakeWindowsAgentBackendRuntimeOwner
        {
            ResetFailure = failure,
            Snapshot = CreateResettableSnapshot()
        };
        var shutdown = new WindowsAgentShutdownCoordinator();
        WindowsAgentShutdownReason? shutdownReason = null;
        shutdown.ShutdownRequested += (_, args) => shutdownReason = args.Reason;
        var coordinator = new WindowsAgentDatabaseResetCoordinator(
            new FakeWindowsAgentEndpointHost(),
            backend,
            shutdown);

        var result = await coordinator.ResetAsync();

        Assert.IsFalse(result.Completed);
        Assert.IsTrue(result.RequiresProcessRestart);
        Assert.AreEqual(1, backend.RequireRestartCount);
        Assert.AreEqual(WindowsAgentShutdownReason.RestartRequired, shutdownReason);
    }

    [TestMethod]
    public async Task ResetRejectedBeforeDestructiveShutdownLeavesEndpointAvailable()
    {
        var endpoint = new FakeWindowsAgentEndpointHost();
        var backend = new FakeWindowsAgentBackendRuntimeOwner();
        var coordinator = new WindowsAgentDatabaseResetCoordinator(
            endpoint,
            backend,
            new WindowsAgentShutdownCoordinator());

        var result = await coordinator.ResetAsync();

        Assert.IsFalse(result.Completed);
        Assert.IsFalse(result.RequiresProcessRestart);
        Assert.AreEqual(0, endpoint.StopCount);
        Assert.AreEqual(0, backend.ResetCount);
        Assert.IsNotNull(result.SafeMessage);
        StringAssert.Contains(result.SafeMessage, "database compatibility failure");
    }

    private static PasswordManagerLocal.Windows.Agent.Backend.WindowsAgentBackendOwnerSnapshot CreateResettableSnapshot() =>
        FakeWindowsAgentBackendRuntimeOwner.CreateSnapshot(
            runtimeState: BackendRuntimeState.Failed,
            runtimeFailureKind: BackendRuntimeFailureKind.DatabaseCompatibility,
            failure: new InvalidOperationException("unsupported database"));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }
}
