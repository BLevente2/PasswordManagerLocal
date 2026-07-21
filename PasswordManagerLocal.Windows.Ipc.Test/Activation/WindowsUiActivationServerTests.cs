using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Windows.Activation;
using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

namespace PasswordManagerLocal.Windows.Ipc.Test.Activation;

[TestClass]
public sealed class WindowsUiActivationServerTests
{
    [TestMethod]
    public async Task ActivationRequestReachesBridge()
    {
        var bridge = new FakeWindowsWindowActivationBridge();
        var sink = new WindowsUiActivationRequestSink(bridge);

        var accepted = await sink.RequestActivationAsync(
            new UiActivationRequestDto(UiActivationReason.TrayIcon, BringToForeground: true),
            CancellationToken.None);

        Assert.IsTrue(accepted);
        Assert.AreEqual(1, bridge.ActivationCount);
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
