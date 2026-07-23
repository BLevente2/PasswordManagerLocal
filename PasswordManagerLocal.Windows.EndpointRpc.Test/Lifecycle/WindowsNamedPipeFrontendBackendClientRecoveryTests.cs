using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Runtime.Abstractions;
using PasswordManagerLocal.Windows.EndpointRpc.Client;
using PasswordManagerLocal.Windows.EndpointRpc.Test.Infrastructure;
using PasswordManagerLocal.Windows.Ipc.Contracts;

namespace PasswordManagerLocal.Windows.EndpointRpc.Test.Lifecycle;

[TestClass]
public sealed class WindowsNamedPipeFrontendBackendClientRecoveryTests
{
    [TestMethod]
    public async Task StartupCompatibilityFailurePreservesResettableFailureKind()
    {
        var agent = new FakeEndpointRpcAgentConnection
        {
            BackendStatus = new BackendRuntimeStatusDto(
                BackendRuntimeStatusState.Failed,
                BackendRuntimeFailureStatusKind.DatabaseCompatibility,
                null,
                RequiresProcessRestart: false,
                DateTimeOffset.UtcNow)
        };
        var client = new WindowsNamedPipeFrontendBackendClient(
            agent,
            new SequenceEndpointRpcClientConnector(),
            maximumRecoveryAttempts: 1,
            recoveryDelay: TimeSpan.Zero);

        await Assert.ThrowsExactlyAsync<DatabaseVersionNotSupportedException>(
            () => client.ConnectAsync());

        Assert.AreEqual(BackendRuntimeFailureKind.DatabaseCompatibility, client.Snapshot.FailureKind);
        Assert.AreEqual(WindowsEndpointClientConnectionState.Unavailable, client.ConnectionState);
        Assert.IsInstanceOfType<DatabaseVersionNotSupportedException>(client.Snapshot.Failure);
        await client.DisposeAsync();
    }

    [TestMethod]
    public async Task RestartRequiredStatusStopsRecoveryWithoutFallbackRuntime()
    {
        var agent = new FakeEndpointRpcAgentConnection
        {
            BackendStatus = new BackendRuntimeStatusDto(
                BackendRuntimeStatusState.Failed,
                BackendRuntimeFailureStatusKind.ShutdownFailure,
                null,
                RequiresProcessRestart: true,
                DateTimeOffset.UtcNow)
        };
        var client = new WindowsNamedPipeFrontendBackendClient(
            agent,
            new SequenceEndpointRpcClientConnector(),
            maximumRecoveryAttempts: 1,
            recoveryDelay: TimeSpan.Zero);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.ConnectAsync());

        Assert.AreEqual(BackendRuntimeFailureKind.ShutdownFailure, client.Snapshot.FailureKind);
        Assert.AreEqual(WindowsEndpointClientConnectionState.Unavailable, client.ConnectionState);
        await client.DisposeAsync();
    }

    [TestMethod]
    public async Task EndpointLossRejectsCallsThenRestoresFutureCallsWithoutReplay()
    {
        var agent = new FakeEndpointRpcAgentConnection();
        var first = new ControllableEndpointRpcTransport();
        var second = new ControllableEndpointRpcTransport();
        var connector = new SequenceEndpointRpcClientConnector(first, second);
        await using var client = new WindowsNamedPipeFrontendBackendClient(
            agent,
            connector,
            maximumRecoveryAttempts: 2,
            recoveryDelay: TimeSpan.Zero);
        await client.ConnectAsync();
        Assert.AreEqual(WindowsEndpointClientConnectionState.Ready, client.ConnectionState);

        first.Disconnect();
        await WaitUntilAsync(() => connector.ConnectCount == 2 && client.Snapshot.State == BackendRuntimeState.Ready);

        Assert.IsTrue(first.IsDisposed);
        Assert.AreEqual(2, connector.ConnectCount);
        Assert.IsNotNull(await client.GetEndpointsAsync());
        Assert.AreEqual(0, agent.ResetCount);
    }

    [TestMethod]
    public async Task ControlLossRunsOneSerializedRecoveryAndReconnectsBothChannels()
    {
        var agent = new FakeEndpointRpcAgentConnection();
        var first = new ControllableEndpointRpcTransport();
        var second = new ControllableEndpointRpcTransport();
        var connector = new SequenceEndpointRpcClientConnector(first, second);
        await using var client = new WindowsNamedPipeFrontendBackendClient(
            agent,
            connector,
            maximumRecoveryAttempts: 3,
            recoveryDelay: TimeSpan.Zero);
        await client.ConnectAsync();

        agent.Disconnect();
        await WaitUntilAsync(() => client.Snapshot.State == BackendRuntimeState.Ready && connector.ConnectCount == 2);

        Assert.AreEqual(2, agent.EnsureCount);
        Assert.AreEqual(2L, agent.ConnectionGeneration);
        Assert.AreEqual(2, connector.ConnectCount);
        Assert.IsTrue(first.IsDisposed);
    }

    [TestMethod]
    public async Task FailedRecoveryIsBoundedAndLeavesClientUnavailable()
    {
        var agent = new FakeEndpointRpcAgentConnection();
        agent.EnqueueEnsureResult(true);
        agent.EnqueueEnsureResult(false);
        agent.EnqueueEnsureResult(false);
        agent.EnqueueEnsureResult(false);
        var first = new ControllableEndpointRpcTransport();
        var connector = new SequenceEndpointRpcClientConnector(first);
        await using var client = new WindowsNamedPipeFrontendBackendClient(
            agent,
            connector,
            maximumRecoveryAttempts: 3,
            recoveryDelay: TimeSpan.Zero);
        await client.ConnectAsync();

        agent.Disconnect();
        await WaitUntilAsync(() => agent.EnsureCount == 4);

        Assert.AreEqual(BackendRuntimeState.Failed, client.Snapshot.State);
        Assert.AreEqual(WindowsEndpointClientConnectionState.Unavailable, client.ConnectionState);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.GetEndpointsAsync());
        Assert.AreEqual(1, connector.ConnectCount);
    }

    [TestMethod]
    public async Task DisposalCancelsAndAwaitsTheSingleRecoveryLoop()
    {
        var agent = new FakeEndpointRpcAgentConnection();
        agent.EnqueueEnsureResult(true);
        agent.EnqueueEnsureResult(false);
        var transport = new ControllableEndpointRpcTransport();
        var client = new WindowsNamedPipeFrontendBackendClient(
            agent,
            new SequenceEndpointRpcClientConnector(transport),
            maximumRecoveryAttempts: 3,
            recoveryDelay: TimeSpan.FromSeconds(30));
        await client.ConnectAsync();

        agent.Disconnect();
        await WaitUntilAsync(() => agent.EnsureCount == 2);
        await client.DisposeAsync();

        Assert.AreEqual(1, agent.DisposeCount);
        Assert.IsTrue(transport.IsDisposed);
        Assert.AreEqual(WindowsEndpointClientConnectionState.Disposed, client.ConnectionState);
    }

    [TestMethod]
    public async Task AgentOrchestratedResetInvalidatesOldEndpointAndReconnects()
    {
        var agent = new FakeEndpointRpcAgentConnection();
        var first = new ControllableEndpointRpcTransport();
        var second = new ControllableEndpointRpcTransport();
        var connector = new SequenceEndpointRpcClientConnector(first, second);
        await using var client = new WindowsNamedPipeFrontendBackendClient(
            agent,
            connector,
            maximumRecoveryAttempts: 2,
            recoveryDelay: TimeSpan.Zero);
        await client.ConnectAsync();

        await client.ResetDatabaseAndRestartAsync();

        Assert.AreEqual(1, agent.ResetCount);
        Assert.IsTrue(first.IsDisposed);
        Assert.AreEqual(2, connector.ConnectCount);
        Assert.AreEqual(BackendRuntimeState.Ready, client.Snapshot.State);
        Assert.AreEqual(WindowsEndpointClientConnectionState.Ready, client.ConnectionState);
    }

    [TestMethod]
    public async Task RejectedResetReconnectsEndpointWithoutReplayingReset()
    {
        var agent = new FakeEndpointRpcAgentConnection
        {
            ResetResult = new(false, false, "The reset was rejected before shutdown.")
        };
        var first = new ControllableEndpointRpcTransport();
        var second = new ControllableEndpointRpcTransport();
        var connector = new SequenceEndpointRpcClientConnector(first, second);
        await using var client = new WindowsNamedPipeFrontendBackendClient(
            agent,
            connector,
            maximumRecoveryAttempts: 2,
            recoveryDelay: TimeSpan.Zero);
        await client.ConnectAsync();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => client.ResetDatabaseAndRestartAsync());
        await WaitUntilAsync(() => connector.ConnectCount == 2 && client.Snapshot.State == BackendRuntimeState.Ready);

        Assert.AreEqual(1, agent.ResetCount);
        Assert.IsTrue(first.IsDisposed);
        Assert.IsNotNull(await client.GetEndpointsAsync());
    }

    [TestMethod]
    public async Task ResetRequiringProcessRestartRecoversThroughCleanAgentWithoutReplay()
    {
        var agent = new FakeEndpointRpcAgentConnection
        {
            ResetResult = new(false, true, "The agent must restart."),
            DisconnectOnReset = true
        };
        var first = new ControllableEndpointRpcTransport();
        var second = new ControllableEndpointRpcTransport();
        var connector = new SequenceEndpointRpcClientConnector(first, second);
        await using var client = new WindowsNamedPipeFrontendBackendClient(
            agent,
            connector,
            maximumRecoveryAttempts: 2,
            recoveryDelay: TimeSpan.Zero);
        await client.ConnectAsync();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => client.ResetDatabaseAndRestartAsync());
        await WaitUntilAsync(() => connector.ConnectCount == 2 &&
            client.ConnectionState == WindowsEndpointClientConnectionState.Ready);

        Assert.AreEqual(1, agent.ResetCount);
        Assert.AreEqual(2L, agent.ConnectionGeneration);
        Assert.AreEqual(2, connector.ConnectCount);
        Assert.IsTrue(first.IsDisposed);
        Assert.IsNotNull(await client.GetEndpointsAsync());
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }
}
