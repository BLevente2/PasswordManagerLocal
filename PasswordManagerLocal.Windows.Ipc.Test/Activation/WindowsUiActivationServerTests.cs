using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Windows.Activation;
using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

namespace PasswordManagerLocal.Windows.Ipc.Test.Activation;

[TestClass]
public sealed class WindowsUiActivationServerTests
{
    [TestMethod]
    public async Task ActivationRequestReachesActivationBridge()
    {
        var activation = new FakeWindowsWindowActivationBridge();
        var shutdown = new FakeWindowsUiShutdownBridge();
        var sink = new WindowsUiActivationRequestSink(activation, shutdown);

        var accepted = await sink.RequestActivationAsync(
            new UiActivationRequestDto(
                UiActivationReason.TrayIcon,
                BringToForeground: true,
                UiActivationCommand.Activate),
            CancellationToken.None);

        Assert.IsTrue(accepted);
        Assert.AreEqual(1, activation.ActivationCount);
        Assert.AreEqual(0, shutdown.ShutdownCount);
    }

    [TestMethod]
    public async Task AgentShutdownRequestReachesOnlyShutdownBridge()
    {
        var activation = new FakeWindowsWindowActivationBridge();
        var shutdown = new FakeWindowsUiShutdownBridge();
        var sink = new WindowsUiActivationRequestSink(activation, shutdown);

        var accepted = await sink.RequestActivationAsync(
            new UiActivationRequestDto(
                UiActivationReason.AgentRequest,
                BringToForeground: false,
                UiActivationCommand.Shutdown),
            CancellationToken.None);

        Assert.IsTrue(accepted);
        Assert.AreEqual(0, activation.ActivationCount);
        Assert.AreEqual(1, shutdown.ShutdownCount);
    }

    [TestMethod]
    public async Task RepeatedAgentShutdownRequestsAreIdempotent()
    {
        var activation = new FakeWindowsWindowActivationBridge();
        var shutdown = new FakeWindowsUiShutdownBridge();
        var sink = new WindowsUiActivationRequestSink(activation, shutdown);
        var request = new UiActivationRequestDto(
            UiActivationReason.AgentRequest,
            BringToForeground: false,
            UiActivationCommand.Shutdown);

        var first = await sink.RequestActivationAsync(request, CancellationToken.None);
        var second = await sink.RequestActivationAsync(request, CancellationToken.None);

        Assert.IsTrue(first);
        Assert.IsTrue(second);
        Assert.AreEqual(1, shutdown.ShutdownCount);
        Assert.AreEqual(0, activation.ActivationCount);
    }

    [TestMethod]
    public async Task DisposalStopsActivationServerAcceptance()
    {
        var host = new FakeWindowsIpcServerHost();
        var server = new WindowsUiActivationServer(host);
        await server.StartAsync();

        await server.DisposeAsync();

        Assert.AreEqual(1, host.StartCount);
        Assert.AreEqual(1, host.StopCount);
        Assert.AreEqual(1, host.DisposeCount);
    }
}
