using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Windows.Agent.Hosting;
using PasswordManagerLocal.Windows.Agent.Status;
using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Lifecycle;
using PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;
using PasswordManagerLocal.Windows.Ipc.Validation;

namespace PasswordManagerLocal.Windows.Ipc.Test.Agent;

[TestClass]
public sealed class WindowsAgentStatusProviderTests
{
    [TestMethod]
    public async Task PhaseFourStatusReportsShellTruthWithoutBackendOwnership()
    {
        var state = new WindowsAgentStateStore();
        state.MarkRunning(DateTimeOffset.UtcNow);
        var coordinator = new SingleUiConnectionCoordinator();
        Assert.IsTrue(coordinator.TryRegister(Guid.NewGuid()));
        var provider = new WindowsAgentStatusProvider(
            state,
            coordinator,
            new FakeWindowsBackgroundSyncSettingsReader { IsEnabled = true });

        var agent = await provider.GetAgentStatusAsync(CancellationToken.None);
        var backend = await provider.GetBackendRuntimeStatusAsync(CancellationToken.None);
        var interactive = await provider.GetInteractiveSessionStatusAsync(CancellationToken.None);
        var synchronization = await provider.GetSynchronizationStatusAsync(CancellationToken.None);

        Assert.AreEqual(AgentState.Running, agent.AgentState);
        Assert.IsTrue(agent.IsUiConnected);
        Assert.IsFalse(agent.BackendOwnedByAgent);
        Assert.IsFalse(agent.IsBackendRunning);
        Assert.IsTrue(agent.IsBackgroundSyncEnabled);
        Assert.AreEqual(BackendRuntimeStatusState.Unavailable, backend.RuntimeState);
        Assert.AreEqual(InteractiveSessionStatusState.None, interactive.LifecycleState);
        Assert.AreEqual(SynchronizationStatusState.Unavailable, synchronization.State);
        var validator = new WindowsIpcContractValidator();
        validator.Validate(agent);
        validator.Validate(backend);
        validator.Validate(interactive);
        validator.Validate(synchronization);
    }

    [TestMethod]
    public async Task SettingsReadFailureDoesNotFakeBackgroundSyncState()
    {
        var state = new WindowsAgentStateStore();
        state.MarkRunning(DateTimeOffset.UtcNow);
        var provider = new WindowsAgentStatusProvider(
            state,
            new SingleUiConnectionCoordinator(),
            new FakeWindowsBackgroundSyncSettingsReader { ThrowOnRead = true });

        var status = await provider.GetAgentStatusAsync(CancellationToken.None);

        Assert.IsFalse(status.IsBackgroundSyncEnabled);
        Assert.IsFalse(status.BackendOwnedByAgent);
    }

    [TestMethod]
    public async Task FailureRemainsVisibleDuringStoppingStatus()
    {
        var state = new WindowsAgentStateStore();
        state.MarkFailed("The shell failed safely.");
        state.MarkStopping();
        var provider = new WindowsAgentStatusProvider(
            state,
            new SingleUiConnectionCoordinator(),
            new FakeWindowsBackgroundSyncSettingsReader());

        var status = await provider.GetAgentStatusAsync(CancellationToken.None);

        Assert.AreEqual(AgentState.Stopping, status.AgentState);
        Assert.IsNotNull(status.LastFailure);
        Assert.AreEqual("The shell failed safely.", status.LastFailure.SafeMessage);
        new WindowsIpcContractValidator().Validate(status);
    }
}
