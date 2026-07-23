using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Windows.Agent.Ui;
using PasswordManagerLocal.Windows.Ipc.Client;
using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

namespace PasswordManagerLocal.Windows.Ipc.Test.Ui;

[TestClass]
public sealed class WindowsUiCloseServiceTests
{
    [TestMethod]
    public async Task RequestsOnlyTheNarrowAgentShutdownCommand()
    {
        var client = new FakeWindowsUiActivationClient();
        var service = new WindowsUiCloseService(client, TimeSpan.FromSeconds(1));

        var acknowledged = await service.RequestCloseAsync();

        Assert.IsTrue(acknowledged);
        Assert.AreEqual(1, client.RequestCount);
        Assert.IsNotNull(client.LastRequest);
        Assert.AreEqual(UiActivationReason.AgentRequest, client.LastRequest.Reason);
        Assert.AreEqual(UiActivationCommand.Shutdown, client.LastRequest.Command);
        Assert.IsFalse(client.LastRequest.BringToForeground);
    }

    [TestMethod]
    public async Task RejectedAcknowledgementUsesSafeFalseFallback()
    {
        var client = new FakeWindowsUiActivationClient
        {
            Result = new UiActivationResult(UiActivationResultKind.Rejected, "rejected")
        };
        var service = new WindowsUiCloseService(client, TimeSpan.FromSeconds(1));

        var acknowledged = await service.RequestCloseAsync();

        Assert.IsFalse(acknowledged);
    }

    [TestMethod]
    public async Task MissingAcknowledgementIsBoundedAndUsesSafeFalseFallback()
    {
        var client = new FakeWindowsUiActivationClient
        {
            Completion = new TaskCompletionSource<UiActivationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var service = new WindowsUiCloseService(client, TimeSpan.FromMilliseconds(25));

        var acknowledged = await service.RequestCloseAsync();

        Assert.IsFalse(acknowledged);
        Assert.AreEqual(1, client.RequestCount);
    }

    [TestMethod]
    public async Task CallerCancellationIsNotReportedAsAcknowledgementTimeout()
    {
        var client = new FakeWindowsUiActivationClient
        {
            Completion = new TaskCompletionSource<UiActivationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var service = new WindowsUiCloseService(client, TimeSpan.FromSeconds(1));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => service.RequestCloseAsync(cancellation.Token));
    }
}
