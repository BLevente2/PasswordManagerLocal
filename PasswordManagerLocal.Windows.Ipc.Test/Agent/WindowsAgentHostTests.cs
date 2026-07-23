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
    public async Task SuccessfulLifecycleOwnsBackendEndpointAndTray()
    {
        var processLock = new FakeProcessInstanceLock();
        var control = new FakeWindowsIpcServerHost();
        var endpoint = new FakeWindowsAgentEndpointHost();
        var backend = new FakeWindowsAgentBackendRuntimeOwner();
        var tray = new FakeTrayIconController();
        var close = new FakeWindowsUiCloseService();
        var state = new WindowsAgentStateStore();
        var observed = new List<AgentState> { state.State };
        state.StateChanged += (_, args) => observed.Add(args.Current);
        await using var host = new WindowsAgentHost(
            processLock,
            control,
            endpoint,
            backend,
            tray,
            new FakeWindowsUiOpenService(),
            close,
            new WindowsAgentShutdownCoordinator(),
            state);

        await host.StartAsync();
        await host.ShutdownAsync();

        CollectionAssert.AreEqual(
            new[] { AgentState.Starting, AgentState.Running, AgentState.Stopping, AgentState.Stopped },
            observed);
        Assert.AreEqual(1, backend.StartCount);
        Assert.AreEqual(1, control.StartCount);
        Assert.AreEqual(1, endpoint.StartCount);
        Assert.AreEqual(1, tray.InitializeCount);
        Assert.AreEqual(1, close.RequestCount);
        Assert.AreEqual(1, backend.StopCount);
        Assert.AreEqual(1, backend.DisposeCount);
        Assert.AreEqual(1, endpoint.StopCount);
        Assert.AreEqual(1, tray.DisposeCount);
        Assert.IsTrue(processLock.IsDisposed);
    }

    [TestMethod]
    public async Task SecondOwnerCreatesNoRuntimeOrIpcHost()
    {
        var processLock = new FakeProcessInstanceLock(isOwner: false);
        var control = new FakeWindowsIpcServerHost();
        var endpoint = new FakeWindowsAgentEndpointHost();
        var backend = new FakeWindowsAgentBackendRuntimeOwner();
        var tray = new FakeTrayIconController();
        await using var host = CreateHost(processLock, control, endpoint, backend, tray);

        await Assert.ThrowsExactlyAsync<ProcessInstanceAlreadyOwnedException>(
            () => host.StartAsync());

        Assert.AreEqual(0, backend.StartCount);
        Assert.AreEqual(0, control.StartCount);
        Assert.AreEqual(0, endpoint.StartCount);
        Assert.AreEqual(0, tray.InitializeCount);
    }

    [TestMethod]
    public async Task RuntimeCreationFailureRollsBackWithoutEndpointStartup()
    {
        var failure = new IOException("runtime creation failed");
        var processLock = new FakeProcessInstanceLock();
        var control = new FakeWindowsIpcServerHost();
        var endpoint = new FakeWindowsAgentEndpointHost();
        var backend = new FakeWindowsAgentBackendRuntimeOwner { StartFailure = failure };
        var tray = new FakeTrayIconController();
        await using var host = CreateHost(processLock, control, endpoint, backend, tray);

        var observed = await Assert.ThrowsExactlyAsync<IOException>(() => host.StartAsync());

        Assert.AreSame(failure, observed);
        Assert.AreEqual(1, backend.StartCount);
        Assert.AreEqual(0, control.StartCount);
        Assert.AreEqual(0, endpoint.StartCount);
        Assert.AreEqual(0, tray.InitializeCount);
        Assert.AreEqual(1, backend.DisposeCount);
        Assert.IsTrue(processLock.IsDisposed);
    }

    [TestMethod]
    public async Task EndpointStartupFailureDisposesRuntimeAndControlHost()
    {
        var failure = new IOException("endpoint listener failed");
        var processLock = new FakeProcessInstanceLock();
        var control = new FakeWindowsIpcServerHost();
        var endpoint = new FakeWindowsAgentEndpointHost { StartFailure = failure };
        var backend = new FakeWindowsAgentBackendRuntimeOwner();
        var tray = new FakeTrayIconController();
        await using var host = CreateHost(processLock, control, endpoint, backend, tray);

        var observed = await Assert.ThrowsExactlyAsync<IOException>(() => host.StartAsync());

        Assert.AreSame(failure, observed);
        Assert.AreEqual(1, backend.StartCount);
        Assert.AreEqual(1, control.StartCount);
        Assert.AreEqual(1, endpoint.StartCount);
        Assert.AreEqual(0, tray.InitializeCount);
        Assert.AreEqual(1, endpoint.StopCount);
        Assert.AreEqual(1, control.StopCount);
        Assert.AreEqual(1, backend.StopCount);
        Assert.AreEqual(1, backend.DisposeCount);
        Assert.IsTrue(processLock.IsDisposed);
    }

    [TestMethod]
    public async Task ShutdownOrderKeepsControlAliveForUiClosureAndReleasesLockLast()
    {
        var operations = new List<string>();
        var processLock = new FakeProcessInstanceLock { OperationLog = operations };
        var control = new FakeWindowsIpcServerHost { OperationLog = operations };
        var endpoint = new FakeWindowsAgentEndpointHost { OperationLog = operations };
        var backend = new FakeWindowsAgentBackendRuntimeOwner { OperationLog = operations };
        var tray = new FakeTrayIconController { OperationLog = operations };
        var close = new FakeWindowsUiCloseService { OperationLog = operations };
        await using var host = new WindowsAgentHost(
            processLock,
            control,
            endpoint,
            backend,
            tray,
            new FakeWindowsUiOpenService(),
            close,
            new WindowsAgentShutdownCoordinator(),
            new WindowsAgentStateStore());
        await host.StartAsync();
        operations.Clear();

        await host.ShutdownAsync();

        CollectionAssert.AreEqual(
            new[]
            {
                "endpoint-stop",
                "ui-close",
                "backend-stop",
                "control-stop",
                "backend-dispose",
                "endpoint-dispose",
                "control-dispose",
                "tray-dispose",
                "lock-release"
            },
            operations);
    }

    [TestMethod]
    public async Task StartupOrdersBackendReadinessBeforeEndpointListener()
    {
        var operations = new List<string>();
        var processLock = new FakeProcessInstanceLock { OperationLog = operations };
        var control = new FakeWindowsIpcServerHost { OperationLog = operations };
        var endpoint = new FakeWindowsAgentEndpointHost { OperationLog = operations };
        var backend = new FakeWindowsAgentBackendRuntimeOwner { OperationLog = operations };
        var tray = new FakeTrayIconController { OperationLog = operations };
        await using var host = CreateHost(processLock, control, endpoint, backend, tray);

        await host.StartAsync();

        CollectionAssert.AreEqual(
            new[]
            {
                "lock-acquire",
                "backend-start",
                "control-start",
                "endpoint-start",
                "tray-start"
            },
            operations);
    }

    [TestMethod]
    public async Task EndpointListenerCannotStartWhenBackendOwnerIsNotReady()
    {
        var processLock = new FakeProcessInstanceLock();
        var control = new FakeWindowsIpcServerHost();
        var endpoint = new FakeWindowsAgentEndpointHost();
        var backend = new FakeWindowsAgentBackendRuntimeOwner
        {
            StateAfterStart = PasswordManagerLocal.Windows.Agent.Backend.WindowsAgentBackendOwnerState.Failed
        };
        var tray = new FakeTrayIconController();
        await using var host = CreateHost(processLock, control, endpoint, backend, tray);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.StartAsync());

        Assert.AreEqual(1, backend.StartCount);
        Assert.AreEqual(1, control.StartCount);
        Assert.AreEqual(0, endpoint.StartCount);
        Assert.AreEqual(0, tray.InitializeCount);
        Assert.AreEqual(1, control.StopCount);
        Assert.IsTrue(processLock.IsDisposed);
    }


    [TestMethod]
    public async Task ThrowingLifecycleObserverCannotPreventRequiredShutdownCleanup()
    {
        var processLock = new FakeProcessInstanceLock();
        var control = new FakeWindowsIpcServerHost();
        var endpoint = new FakeWindowsAgentEndpointHost();
        var backend = new FakeWindowsAgentBackendRuntimeOwner();
        var tray = new FakeTrayIconController();
        var state = new WindowsAgentStateStore();
        state.StateChanged += (_, _) => throw new InvalidOperationException("observer failed");
        await using var host = CreateHost(
            processLock,
            control,
            endpoint,
            backend,
            tray,
            state: state);

        await host.StartAsync();
        await host.ShutdownAsync();

        Assert.AreEqual(AgentState.Stopped, state.State);
        Assert.AreEqual(1, endpoint.StopCount);
        Assert.AreEqual(1, backend.DisposeCount);
        Assert.AreEqual(1, control.DisposeCount);
        Assert.AreEqual(1, tray.DisposeCount);
        Assert.IsTrue(processLock.IsDisposed);
    }

    [TestMethod]
    public async Task ShutdownFailuresAreAggregatedAndNeverReportedAsCleanStopped()
    {
        var operations = new List<string>();
        var processLock = new FakeProcessInstanceLock { OperationLog = operations };
        var control = new FakeWindowsIpcServerHost
        {
            OperationLog = operations,
            DisposeFailure = new IOException("control dispose failed")
        };
        var endpoint = new FakeWindowsAgentEndpointHost
        {
            OperationLog = operations,
            StopFailure = new IOException("endpoint stop failed")
        };
        var backend = new FakeWindowsAgentBackendRuntimeOwner
        {
            OperationLog = operations,
            StopFailure = new IOException("runtime stop failed")
        };
        var tray = new FakeTrayIconController
        {
            OperationLog = operations,
            DisposeFailure = new IOException("tray dispose failed")
        };
        var state = new WindowsAgentStateStore();
        var host = CreateHost(processLock, control, endpoint, backend, tray, state: state);
        await host.StartAsync();
        operations.Clear();

        var failure = await Assert.ThrowsExactlyAsync<AggregateException>(
            () => host.ShutdownAsync());

        Assert.AreEqual(4, failure.Flatten().InnerExceptions.Count);
        Assert.AreEqual(AgentState.Failed, state.State);
        Assert.IsNotNull(state.LastFailure);
        Assert.IsTrue(state.LastFailure.RequiresProcessRestart);
        Assert.AreEqual(1, backend.DisposeCount);
        Assert.AreEqual(1, endpoint.DisposeCount);
        Assert.AreEqual(1, control.DisposeCount);
        Assert.AreEqual(1, tray.DisposeCount);
        Assert.IsTrue(processLock.IsDisposed);
        Assert.AreEqual("lock-release", operations[^1]);

        await Assert.ThrowsExactlyAsync<AggregateException>(
            () => host.DisposeAsync().AsTask());
    }

    [TestMethod]
    public async Task TrayExitQueuesShutdownWithoutBlockingForEndpointDrain()
    {
        var stopRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var endpoint = new FakeWindowsAgentEndpointHost { StopRelease = stopRelease };
        var tray = new FakeTrayIconController();
        var state = new WindowsAgentStateStore();
        var stopped = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        state.StateChanged += (_, args) =>
        {
            if (args.Current == AgentState.Stopped)
                stopped.TrySetResult();
        };
        await using var host = CreateHost(
            new FakeProcessInstanceLock(),
            new FakeWindowsIpcServerHost(),
            endpoint,
            new FakeWindowsAgentBackendRuntimeOwner(),
            tray,
            state: state);
        await host.StartAsync();

        tray.RaiseExit();
        await WaitUntilAsync(() => endpoint.StopCount == 1);

        Assert.AreEqual(AgentState.Stopping, state.State);
        Assert.IsFalse(stopped.Task.IsCompleted);
        stopRelease.TrySetResult();
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }


    [TestMethod]
    public async Task TrayTriggeredShutdownFailureIsReflectedAsFatalShellState()
    {
        var endpoint = new FakeWindowsAgentEndpointHost
        {
            StopFailure = new IOException("endpoint drain failed")
        };
        var tray = new FakeTrayIconController();
        var state = new WindowsAgentStateStore();
        var failed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        state.StateChanged += (_, args) =>
        {
            if (args.Current == AgentState.Failed)
                failed.TrySetResult();
        };
        var host = CreateHost(
            new FakeProcessInstanceLock(),
            new FakeWindowsIpcServerHost(),
            endpoint,
            new FakeWindowsAgentBackendRuntimeOwner(),
            tray,
            state: state);
        await host.StartAsync();

        tray.RaiseExit();
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(AgentState.Failed, state.State);
        Assert.IsNotNull(state.LastFailure);
        Assert.IsTrue(state.LastFailure.RequiresProcessRestart);
        Assert.AreEqual(1, endpoint.DisposeCount);
        await Assert.ThrowsExactlyAsync<IOException>(
            () => host.DisposeAsync().AsTask());
    }

    [TestMethod]
    public async Task FatalEndpointFailureStopsAgent()
    {
        var endpoint = new FakeWindowsAgentEndpointHost();
        var state = new WindowsAgentStateStore();
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new List<AgentState>();
        state.StateChanged += (_, args) =>
        {
            observed.Add(args.Current);
            if (args.Current == AgentState.Stopped)
                stopped.TrySetResult();
        };
        await using var host = CreateHost(
            new FakeProcessInstanceLock(),
            new FakeWindowsIpcServerHost(),
            endpoint,
            new FakeWindowsAgentBackendRuntimeOwner(),
            new FakeTrayIconController(),
            state: state);
        await host.StartAsync();

        endpoint.Fail(new IOException("fatal endpoint failure"));
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(AgentState.Stopped, state.State);
        Assert.IsTrue(observed.Contains(AgentState.Failed));
    }

    [TestMethod]
    public async Task TrayOpenAndRepeatedExitAreIdempotent()
    {
        var tray = new FakeTrayIconController();
        var open = new FakeWindowsUiOpenService();
        var close = new FakeWindowsUiCloseService();
        var backend = new FakeWindowsAgentBackendRuntimeOwner();
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
            new FakeWindowsAgentEndpointHost(),
            backend,
            tray,
            open,
            close,
            new WindowsAgentShutdownCoordinator(),
            state);
        await host.StartAsync();

        tray.RaiseOpen();
        await WaitUntilAsync(() => open.OpenCount == 1);
        tray.RaiseExit();
        tray.RaiseExit();
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(1, open.OpenCount);
        Assert.AreEqual(1, close.RequestCount);
        Assert.AreEqual(1, backend.StopCount);
        Assert.AreEqual(1, backend.DisposeCount);
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
        await using var host = CreateHost(
            new FakeProcessInstanceLock(),
            new FakeWindowsIpcServerHost(),
            new FakeWindowsAgentEndpointHost(),
            new FakeWindowsAgentBackendRuntimeOwner(),
            tray,
            state: state);

        var startup = host.StartAsync();
        await WaitUntilAsync(() => tray.InitializeCount == 1);
        var shutdown = host.ShutdownAsync();
        trayRelease.TrySetResult();
        await Task.WhenAll(startup, shutdown);

        Assert.IsFalse(observed.Contains(AgentState.Running));
        Assert.AreEqual(AgentState.Stopped, state.State);
        Assert.AreEqual(1, tray.DisposeCount);
    }

    private static WindowsAgentHost CreateHost(
        FakeProcessInstanceLock processLock,
        FakeWindowsIpcServerHost control,
        FakeWindowsAgentEndpointHost endpoint,
        FakeWindowsAgentBackendRuntimeOwner backend,
        FakeTrayIconController tray,
        FakeWindowsUiOpenService? open = null,
        FakeWindowsUiCloseService? close = null,
        WindowsAgentStateStore? state = null) =>
        new(
            processLock,
            control,
            endpoint,
            backend,
            tray,
            open ?? new FakeWindowsUiOpenService(),
            close ?? new FakeWindowsUiCloseService(),
            new WindowsAgentShutdownCoordinator(),
            state ?? new WindowsAgentStateStore());

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }
}
