using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;
using PasswordManagerLocal.Windows.Settings;

namespace PasswordManagerLocal.Windows.Ipc.Test.Ui;

[TestClass]
public sealed class WindowsAgentBackgroundSyncSettingsClientTests
{
    [TestMethod]
    public async Task LoadReturnsAuthoritativeAgentState()
    {
        var connection = new FakeWindowsAgentControlConnection
        {
            BackgroundState = FakeWindowsBackgroundSyncCoordinator.OperationalState()
        };
        var client = new WindowsAgentBackgroundSyncSettingsClient(connection);

        var state = await client.GetStateAsync();

        Assert.IsTrue(state.IsEnabled);
        Assert.IsTrue(state.IsAvailable);
        Assert.IsFalse(state.IsDegraded);
        Assert.AreEqual(1, connection.GetBackgroundCount);
    }

    [TestMethod]
    public async Task AgentUnavailableReturnsUnavailableStateWithoutInventingValue()
    {
        var connection = new FakeWindowsAgentControlConnection
        {
            EnsureConnectedResult = false
        };
        var client = new WindowsAgentBackgroundSyncSettingsClient(connection);

        var state = await client.GetStateAsync();

        Assert.IsFalse(state.IsAvailable);
        Assert.IsFalse(state.IsEnabled);
        Assert.AreEqual(0, connection.GetBackgroundCount);
    }

    [TestMethod]
    public async Task SuccessfulMutationSendsDesiredBooleanOnce()
    {
        var connection = new FakeWindowsAgentControlConnection();
        var client = new WindowsAgentBackgroundSyncSettingsClient(connection);

        var result = await client.SetEnabledAsync(true);

        Assert.AreEqual(true, connection.LastRequestedEnabled);
        Assert.AreEqual(1, connection.SetBackgroundCount);
        Assert.IsFalse(result.WasOutcomeUncertain);
        Assert.IsTrue(result.State.IsEnabled);
    }

    [TestMethod]
    public async Task LostMutationResponseReconnectsAndReadsBackWithoutReplay()
    {
        var connection = new FakeWindowsAgentControlConnection
        {
            MutateBeforeSetFailure = true,
            SetBackgroundFailure = new IOException("response lost")
        };
        var client = new WindowsAgentBackgroundSyncSettingsClient(connection);

        var result = await client.SetEnabledAsync(true);

        Assert.IsTrue(result.WasOutcomeUncertain);
        Assert.IsTrue(result.State.IsEnabled);
        Assert.AreEqual(1, connection.SetBackgroundCount);
        Assert.AreEqual(1, connection.DisconnectCount);
        Assert.AreEqual(1, connection.GetBackgroundCount);
    }
}
