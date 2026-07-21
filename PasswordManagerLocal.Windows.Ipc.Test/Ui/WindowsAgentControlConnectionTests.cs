using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Windows.AgentConnection;
using PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

namespace PasswordManagerLocal.Windows.Ipc.Test.Ui;

[TestClass]
public sealed class WindowsAgentControlConnectionTests
{
    [TestMethod]
    public async Task ExistingAgentIsReusedWithoutLaunch()
    {
        var connector = new FakeWindowsAgentControlConnector();
        var registered = new FakeWindowsAgentRegisteredConnection();
        connector.Enqueue(registered);
        var launcher = new FakeWindowsAgentLauncher();
        await using var connection = new WindowsAgentControlConnection(
            "control-pipe",
            launcher,
            connector,
            retryDelay: TimeSpan.Zero);

        var connected = await connection.ConnectAsync();
        var connectedAgain = await connection.ConnectAsync();

        Assert.IsTrue(connected);
        Assert.IsTrue(connectedAgain);
        Assert.AreEqual(1, connector.AttemptCount);
        Assert.AreEqual(0, launcher.LaunchCount);
    }

    [TestMethod]
    public async Task MissingAgentIsLaunchedThenUiRegisters()
    {
        var connector = new FakeWindowsAgentControlConnector();
        var registered = new FakeWindowsAgentRegisteredConnection();
        connector.Enqueue(null);
        connector.Enqueue(registered);
        var launcher = new FakeWindowsAgentLauncher();
        await using var connection = new WindowsAgentControlConnection(
            "control-pipe",
            launcher,
            connector,
            maximumConnectionAttempts: 3,
            retryDelay: TimeSpan.Zero);

        var connected = await connection.ConnectAsync();

        Assert.IsTrue(connected);
        Assert.IsTrue(connection.IsConnected);
        Assert.AreEqual(1, launcher.LaunchCount);
        Assert.AreEqual(2, connector.AttemptCount);
    }

    [TestMethod]
    public async Task RetryAfterLaunchIsBounded()
    {
        var connector = new FakeWindowsAgentControlConnector();
        var launcher = new FakeWindowsAgentLauncher();
        await using var connection = new WindowsAgentControlConnection(
            "control-pipe",
            launcher,
            connector,
            maximumConnectionAttempts: 3,
            retryDelay: TimeSpan.Zero);

        var connected = await connection.ConnectAsync();

        Assert.IsFalse(connected);
        Assert.AreEqual(1, launcher.LaunchCount);
        Assert.AreEqual(4, connector.AttemptCount);
    }

    [TestMethod]
    public async Task AgentLaunchFailureProducesDegradedConnectionState()
    {
        var connector = new FakeWindowsAgentControlConnector();
        var launcher = new FakeWindowsAgentLauncher { LaunchResult = false };
        await using var connection = new WindowsAgentControlConnection(
            "control-pipe",
            launcher,
            connector,
            retryDelay: TimeSpan.Zero);

        var connected = await connection.ConnectAsync();

        Assert.IsFalse(connected);
        Assert.IsFalse(connection.IsConnected);
        Assert.AreEqual(1, connector.AttemptCount);
        Assert.AreEqual(1, launcher.LaunchCount);
    }

    [TestMethod]
    public async Task DisposalUnregistersAndDisposesPersistentConnectionOnce()
    {
        var connector = new FakeWindowsAgentControlConnector();
        var registered = new FakeWindowsAgentRegisteredConnection();
        connector.Enqueue(registered);
        var connection = new WindowsAgentControlConnection(
            "control-pipe",
            new FakeWindowsAgentLauncher(),
            connector,
            retryDelay: TimeSpan.Zero);
        Assert.IsTrue(await connection.ConnectAsync());

        await connection.DisposeAsync();
        await connection.DisposeAsync();

        Assert.AreEqual(1, registered.DisposeCount);
        Assert.IsFalse(connection.IsConnected);
    }
}
