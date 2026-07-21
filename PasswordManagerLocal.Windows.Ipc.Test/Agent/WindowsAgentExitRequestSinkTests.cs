using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Windows.Agent.Hosting;
using PasswordManagerLocal.Windows.Ipc.Contracts;

namespace PasswordManagerLocal.Windows.Ipc.Test.Agent;

[TestClass]
public sealed class WindowsAgentExitRequestSinkTests
{
    [TestMethod]
    public async Task AcceptedExitRequestBeginsCoordinatedShutdownOnce()
    {
        var coordinator = new WindowsAgentShutdownCoordinator();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCount = 0;
        coordinator.ShutdownRequested += (_, _) =>
        {
            requestCount++;
            completion.TrySetResult();
        };
        var sink = new WindowsAgentExitRequestSink(coordinator, TimeSpan.Zero);

        Assert.IsTrue(await sink.RequestExitAsync(
            new AgentExitRequestDto(AgentExitReason.UserRequested),
            CancellationToken.None));
        Assert.IsTrue(await sink.RequestExitAsync(
            new AgentExitRequestDto(AgentExitReason.UserRequested),
            CancellationToken.None));
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(1, requestCount);
    }
}
