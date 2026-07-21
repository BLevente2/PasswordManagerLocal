using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Windows.Agent.Hosting;
using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Coordination;
using PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

namespace PasswordManagerLocal.Windows.Ipc.Test.Agent;

[TestClass]
public sealed class WindowsAgentHostTests
{
    [TestMethod]
    public async Task SuccessfulLifecycleReachesRunningStoppingAndStopped()
    {
        var processLock = new FakeProcessInstanceLock();
        var server = new FakeWindowsIpcServerHost();
        var tray = new FakeTrayIconController();
        var open = new FakeWindowsUiOpenService();
        var shutdown = new WindowsAgentShutdownCoordinator();
        var state = new WindowsAgentStateStore();
        var observed = new List<AgentState> { state.State };
        state.StateChanged += (_, args) => observed.Add(args.Current);
        await using var host = new WindowsAgentHost(
            processLock, server, tray, open, shutdown, state);

        await host.StartAsync();
        await host.ShutdownAsync();

        CollectionAssert.AreEqual(
            new[] { AgentState.Starting, AgentState.Running, AgentState.Stopping, AgentState.Stopped },
            observed);
        Assert.AreEqual(1, server.StartCount);
        Assert.AreEqual(1, tray.InitializeCount);
        Assert.AreEqual(1, tray.DisposeCount);
        Assert.IsTrue(processLock.IsDisposed);
    }

    [TestMethod]
    public async Task SecondOwnerDoesNotStartTrayOrServer()
    {
        var processLock = new FakeProcessInstanceLock(isOwner: false);
        var server = new FakeWindowsIpcServerHost();
        var tray = new FakeTrayIconController();
        await using var host = new WindowsAgentHost(
            processLock,
            server,
            tray,
            new FakeWindowsUiOpenService(),
            new WindowsAgentShutdownCoordinator(),
            new WindowsAgentStateStore());

        await Assert.ThrowsExactlyAsync<ProcessInstanceAlreadyOwnedException>(
            () => host.StartAsync());

        Assert.AreEqual(0, server.StartCount);
        Assert.AreEqual(0, tray.InitializeCount);
    }

    [TestMethod]
    public async Task StartupFailureTransitionsThroughFailedAndReleasesOwnership()
    {
        var processLock = new FakeProcessInstanceLock();
        var server = new FakeWindowsIpcServerHost { ThrowOnStart = true };
        var state = new WindowsAgentStateStore();
        var observed = new List<AgentState> { state.State };
        state.StateChanged += (_, args) => observed.Add(args.Current);
        await using var host = new WindowsAgentHost(
            processLock,
            server,
            new FakeTrayIconController(),
            new FakeWindowsUiOpenService(),
            new WindowsAgentShutdownCoordinator(),
            state);

        await Assert.ThrowsExactlyAsync<IOException>(() => host.StartAsync());

        CollectionAssert.Contains(observed, AgentState.Failed);
        CollectionAssert.Contains(observed, AgentState.Stopping);
        Assert.AreEqual(AgentState.Stopped, state.State);
        Assert.IsTrue(processLock.IsDisposed);
    }

    [TestMethod]
    public async Task FatalListenerFailureStopsAgentWithoutRepeatedShutdownFailure()
    {
        var server = new FakeWindowsIpcServerHost();
        var state = new WindowsAgentStateStore();
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        state.StateChanged += (_, args) =>
        {
            if (args.Current == AgentState.Stopped)
                stopped.TrySetResult();
        };
        await using var host = new WindowsAgentHost(
            new FakeProcessInstanceLock(),
            server,
            new FakeTrayIconController(),
            new FakeWindowsUiOpenService(),
            new WindowsAgentShutdownCoordinator(),
            state);
        await host.StartAsync();

        server.FailListener(new IOException("fatal listener failure"));
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await host.ShutdownAsync();

        Assert.AreEqual(AgentState.Stopped, state.State);
        Assert.AreEqual(1, server.StopCount);
    }

    [TestMethod]
    public async Task TrayOpenAndExitUseInternalServicesDirectly()
    {
        var tray = new FakeTrayIconController();
        var open = new FakeWindowsUiOpenService();
        var state = new WindowsAgentStateStore();
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        state.StateChanged += (_, args) =>
        {
            if (args.Current == AgentState.Stopped)
                stopped.TrySetResult();
        };
        await using var host = new WindowsAgentHost(
            new FakeProcessInstanceLock(),
            new FakeWindowsIpcServerHost(),
            tray,
            open,
            new WindowsAgentShutdownCoordinator(),
            state);
        await host.StartAsync();

        tray.RaiseOpen();
        await WaitUntilAsync(() => open.OpenCount == 1);
        tray.RaiseExit();
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(1, open.OpenCount);
    }


    [TestMethod]
    public async Task UnexpectedOwnershipFailureProducesStartupFailureWithoutTrayOrServer()
    {
        var processLock = new FakeProcessInstanceLock
        {
            EnsureOwnershipFailure = new IOException("ownership failure")
        };
        var server = new FakeWindowsIpcServerHost();
        var tray = new FakeTrayIconController();
        await using var host = new WindowsAgentHost(
            processLock,
            server,
            tray,
            new FakeWindowsUiOpenService(),
            new WindowsAgentShutdownCoordinator(),
            new WindowsAgentStateStore());

        await Assert.ThrowsExactlyAsync<IOException>(() => host.StartAsync());

        Assert.AreEqual(0, server.StartCount);
        Assert.AreEqual(0, tray.InitializeCount);
        Assert.IsTrue(processLock.IsDisposed);
    }

    [TestMethod]
    public async Task ShutdownDuringStartupCannotTransitionBackToRunning()
    {
        var trayRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var tray = new FakeTrayIconController { InitializationRelease = trayRelease };
        var state = new WindowsAgentStateStore();
        var observed = new List<AgentState> { state.State };
        state.StateChanged += (_, args) => observed.Add(args.Current);
        await using var host = new WindowsAgentHost(
            new FakeProcessInstanceLock(),
            new FakeWindowsIpcServerHost(),
            tray,
            new FakeWindowsUiOpenService(),
            new WindowsAgentShutdownCoordinator(),
            state);

        var startup = host.StartAsync();
        await WaitUntilAsync(() => tray.InitializeCount == 1);
        var shutdown = host.ShutdownAsync();
        trayRelease.TrySetResult();
        await Task.WhenAll(startup, shutdown);

        Assert.IsFalse(observed.Contains(AgentState.Running));
        Assert.AreEqual(AgentState.Stopped, state.State);
        Assert.AreEqual(1, tray.DisposeCount);
    }

    [TestMethod]
    public async Task RepeatedShutdownIsSafeAndReleasesOwnershipOnce()
    {
        var processLock = new FakeProcessInstanceLock();
        var server = new FakeWindowsIpcServerHost();
        var tray = new FakeTrayIconController();
        await using var host = new WindowsAgentHost(
            processLock,
            server,
            tray,
            new FakeWindowsUiOpenService(),
            new WindowsAgentShutdownCoordinator(),
            new WindowsAgentStateStore());
        await host.StartAsync();

        await Task.WhenAll(host.ShutdownAsync(), host.ShutdownAsync());

        Assert.AreEqual(1, server.StopCount);
        Assert.AreEqual(1, tray.DisposeCount);
        Assert.IsTrue(processLock.IsDisposed);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }
}
